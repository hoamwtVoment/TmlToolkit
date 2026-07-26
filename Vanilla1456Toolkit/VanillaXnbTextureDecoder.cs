using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Terraria1456Toolkit
{
    /// <summary>
    /// Reads Terraria's vanilla item XNB files without touching GraphicsDevice.
    /// XNA performs its own LZX decompression; this class only parses the verified
    /// Texture2D payload and converts its premultiplied RGBA pixels to a GDI bitmap.
    /// </summary>
    internal static class VanillaXnbTextureDecoder
    {
        private const int MaximumTextureDimension = 4096;
        private const int MaximumDecodedBytes = 64 * 1024 * 1024;
        private const int MaximumTypeReaders = 1024;
        private const int MaximumTypeReaderNameBytes = 16 * 1024;
        private const string XnaAssemblyName =
            "Microsoft.Xna.Framework, Version=4.0.0.0, Culture=neutral, " +
            "PublicKeyToken=842cf8be1de50553";

        private static readonly object ResolverSync = new object();
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);
        private static MethodInfo _prepareXnbStream;
        private static int[] _textureCopyLoad;

        public static Bitmap DecodeItemTexture(
            string terrariaDirectory,
            int itemType)
        {
            if (String.IsNullOrEmpty(terrariaDirectory))
                throw new ArgumentNullException("terrariaDirectory");
            if (itemType <= 0)
                throw new ArgumentOutOfRangeException("itemType");

            string path = GetItemTexturePath(terrariaDirectory, itemType);
            if (!File.Exists(path))
            {
                int sourceType = ResolveTextureSourceItemType(itemType);
                if (sourceType != itemType)
                    path = GetItemTexturePath(terrariaDirectory, sourceType);
            }

            return DecodeFile(path);
        }

        public static bool TryDecodeItemTexture(
            string terrariaDirectory,
            int itemType,
            out Bitmap bitmap,
            out string error)
        {
            bitmap = null;
            error = null;
            try
            {
                bitmap = DecodeItemTexture(terrariaDirectory, itemType);
                return true;
            }
            catch (Exception ex)
            {
                error = DescribeException(ex);
                return false;
            }
        }

        public static Bitmap DecodeFile(string xnbPath)
        {
            if (String.IsNullOrEmpty(xnbPath))
                throw new ArgumentNullException("xnbPath");

            using (FileStream input = new FileStream(
                xnbPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                return DecodeCore(input, xnbPath);
            }
        }

        public static bool TryDecodeFile(
            string xnbPath,
            out Bitmap bitmap,
            out string error)
        {
            bitmap = null;
            error = null;
            try
            {
                bitmap = DecodeFile(xnbPath);
                return true;
            }
            catch (Exception ex)
            {
                error = DescribeException(ex);
                return false;
            }
        }

        /// <summary>
        /// Decodes an XNB from the stream's current position. The caller retains
        /// ownership of the input stream.
        /// </summary>
        public static Bitmap Decode(Stream input)
        {
            if (input == null)
                throw new ArgumentNullException("input");
            return DecodeCore(input, "<stream>");
        }

        private static Bitmap DecodeCore(Stream input, string assetName)
        {
            if (!input.CanRead)
                throw new ArgumentException(
                    "The XNB stream is not readable.", "input");

            NonClosingReadStream protectedInput =
                new NonClosingReadStream(input);
            Stream payload = null;
            try
            {
                object[] arguments = new object[] {
                    protectedInput,
                    assetName,
                    0
                };

                try
                {
                    payload = (Stream)GetPrepareXnbStream().Invoke(
                        null, arguments);
                }
                catch (TargetInvocationException ex)
                {
                    Exception cause = ex.InnerException ?? ex;
                    throw new InvalidDataException(
                        "XNA could not open the XNB payload: " +
                        cause.Message,
                        cause);
                }

                if (payload == null)
                    throw new InvalidDataException(
                        "XNA returned no XNB payload stream.");

                return DecodeTextureBody(payload);
            }
            finally
            {
                if (payload != null)
                    payload.Dispose();
                else
                    protectedInput.Dispose();
            }
        }

        private static MethodInfo GetPrepareXnbStream()
        {
            MethodInfo method = _prepareXnbStream;
            if (method != null)
                return method;

            lock (ResolverSync)
            {
                if (_prepareXnbStream != null)
                    return _prepareXnbStream;

                Assembly xna = FindLoadedAssembly(
                    "Microsoft.Xna.Framework");
                if (xna == null)
                    xna = Assembly.Load(XnaAssemblyName);

                Type readerType = xna.GetType(
                    "Microsoft.Xna.Framework.Content.ContentReader",
                    true,
                    false);
                Type streamType = typeof(Stream);
                Type intByRef = typeof(int).MakeByRefType();
                method = readerType.GetMethod(
                    "PrepareStream",
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Static,
                    null,
                    new Type[] {
                        streamType,
                        typeof(string),
                        intByRef
                    },
                    null);

                if (method == null ||
                    !streamType.IsAssignableFrom(method.ReturnType))
                {
                    throw new MissingMethodException(
                        readerType.FullName,
                        "PrepareStream(Stream, string, out int)");
                }

                _prepareXnbStream = method;
                return method;
            }
        }

        private static Bitmap DecodeTextureBody(Stream payload)
        {
            BinaryReader reader = new BinaryReader(
                payload, Encoding.UTF8, true);

            int readerCount = Read7BitEncodedInt(
                reader, "type reader count");
            if (readerCount <= 0 || readerCount > MaximumTypeReaders)
                throw new InvalidDataException(
                    "The XNB type-reader table is invalid.");

            string[] readers = new string[readerCount];
            for (int i = 0; i < readerCount; i++)
            {
                readers[i] = ReadUtf8String(
                    reader, "type reader name");
                ReadInt32(reader, "type reader version");
            }

            int sharedResources = Read7BitEncodedInt(
                reader, "shared resource count");
            if (sharedResources != 0)
                throw new NotSupportedException(
                    "Item Texture2D XNB files with shared resources " +
                    "are not supported.");

            int rootReaderIndex = Read7BitEncodedInt(
                reader, "root object type reader");
            if (rootReaderIndex <= 0 ||
                rootReaderIndex > readerCount)
            {
                throw new InvalidDataException(
                    "The XNB root reader index is invalid.");
            }

            string rootReader =
                readers[rootReaderIndex - 1] ?? String.Empty;
            if (rootReader.IndexOf(
                    "Texture2DReader",
                    StringComparison.Ordinal) < 0)
            {
                throw new InvalidDataException(
                    "The XNB root object is not Texture2D: " +
                    rootReader);
            }

            int surfaceFormat = ReadInt32(
                reader, "texture surface format");
            int width = ReadInt32(reader, "texture width");
            int height = ReadInt32(reader, "texture height");
            int mipLevels = ReadInt32(
                reader, "texture mip level count");

            if (surfaceFormat != 0)
                throw new NotSupportedException(
                    "Unsupported Texture2D SurfaceFormat value: " +
                    surfaceFormat.ToString() +
                    ". Terraria 1.4.5.6 item textures use Color (0).");
            if (width <= 0 || height <= 0 ||
                width > MaximumTextureDimension ||
                height > MaximumTextureDimension)
            {
                throw new InvalidDataException(
                    "The XNB texture dimensions are outside the " +
                    "supported range.");
            }
            if (mipLevels != 1)
                throw new NotSupportedException(
                    "Unsupported item texture mip count: " +
                    mipLevels.ToString() + ".");

            int requiredBytes = CheckedPixelByteCount(
                width, height);
            int storedBytes = ReadInt32(
                reader, "texture pixel byte count");
            if (storedBytes != requiredBytes)
            {
                throw new InvalidDataException(
                    "The Color texture contains " +
                    storedBytes.ToString() +
                    " pixel bytes; expected " +
                    requiredBytes.ToString() + ".");
            }

            byte[] rgba = ReadExactly(
                reader,
                storedBytes,
                "texture pixels");
            return CreateBitmap(width, height, rgba);
        }

        private static Bitmap CreateBitmap(
            int width,
            int height,
            byte[] premultipliedRgba)
        {
            Bitmap bitmap = new Bitmap(
                width,
                height,
                PixelFormat.Format32bppPArgb);
            BitmapData bits = null;
            try
            {
                Rectangle rectangle = new Rectangle(
                    0, 0, width, height);
                bits = bitmap.LockBits(
                    rectangle,
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppPArgb);

                byte[] bgraRow = new byte[checked(width * 4)];
                for (int y = 0; y < height; y++)
                {
                    int sourceRow = checked(y * width * 4);
                    for (int x = 0; x < width; x++)
                    {
                        int source = sourceRow + x * 4;
                        int destination = x * 4;
                        bgraRow[destination] =
                            premultipliedRgba[source + 2];
                        bgraRow[destination + 1] =
                            premultipliedRgba[source + 1];
                        bgraRow[destination + 2] =
                            premultipliedRgba[source];
                        bgraRow[destination + 3] =
                            premultipliedRgba[source + 3];
                    }

                    Marshal.Copy(
                        bgraRow,
                        0,
                        Add(bits.Scan0, bits.Stride * y),
                        bgraRow.Length);
                }

                bitmap.UnlockBits(bits);
                bits = null;
                return bitmap;
            }
            catch
            {
                if (bits != null)
                    bitmap.UnlockBits(bits);
                bitmap.Dispose();
                throw;
            }
        }

        private static string GetItemTexturePath(
            string terrariaDirectory,
            int itemType)
        {
            return Path.Combine(
                terrariaDirectory,
                "Content",
                "Images",
                "Item_" + itemType.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) +
                ".xnb");
        }

        private static int ResolveTextureSourceItemType(int itemType)
        {
            int[] copies = GetTextureCopyLoad();
            if (copies == null)
                return itemType;

            int current = itemType;
            for (int i = 0; i < 16; i++)
            {
                if (current < 0 || current >= copies.Length)
                    break;
                int next = copies[current];
                if (next < 0 || next == current)
                    break;
                current = next;
            }
            return current;
        }

        private static int[] GetTextureCopyLoad()
        {
            int[] copies = _textureCopyLoad;
            if (copies != null)
                return copies;

            lock (ResolverSync)
            {
                if (_textureCopyLoad != null)
                    return _textureCopyLoad;

                try
                {
                    Assembly terraria = FindLoadedAssembly("Terraria");
                    if (terraria == null)
                        return null;

                    Type sets = terraria.GetType(
                        "Terraria.ID.ItemID+Sets",
                        false,
                        false);
                    if (sets == null)
                        return null;

                    FieldInfo field = sets.GetField(
                        "TextureCopyLoad",
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Static);
                    if (field == null)
                        return null;

                    copies = field.GetValue(null) as int[];
                    if (copies != null)
                        _textureCopyLoad = copies;
                    return copies;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static Assembly FindLoadedAssembly(string simpleName)
        {
            Assembly[] assemblies =
                AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                AssemblyName name;
                try
                {
                    name = assemblies[i].GetName();
                }
                catch
                {
                    continue;
                }

                if (String.Equals(
                        name.Name,
                        simpleName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return assemblies[i];
                }
            }
            return null;
        }

        private static string ReadUtf8String(
            BinaryReader reader,
            string field)
        {
            int byteCount = Read7BitEncodedInt(reader, field + " length");
            if (byteCount < 0 ||
                byteCount > MaximumTypeReaderNameBytes)
            {
                throw new InvalidDataException(
                    "The " + field + " is too long.");
            }

            byte[] bytes = ReadExactly(reader, byteCount, field);
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException(
                    "The " + field + " is not valid UTF-8.", ex);
            }
        }

        private static int Read7BitEncodedInt(
            BinaryReader reader,
            string field)
        {
            int value = 0;
            int shift = 0;
            for (int i = 0; i < 5; i++)
            {
                int current = ReadRequiredByte(reader, field);
                if (i == 4 && (current & 0xF0) != 0)
                    throw new InvalidDataException(
                        "Invalid 7-bit encoded " + field + ".");

                value |= (current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                    return value;
                shift += 7;
            }

            throw new InvalidDataException(
                "Invalid 7-bit encoded " + field + ".");
        }

        private static int ReadRequiredByte(
            BinaryReader reader,
            string field)
        {
            int value = reader.BaseStream.ReadByte();
            if (value < 0)
                throw new EndOfStreamException(
                    "Unexpected end of XNB while reading " +
                    field + ".");
            return value;
        }

        private static int ReadInt32(
            BinaryReader reader,
            string field)
        {
            byte[] bytes = ReadExactly(reader, 4, field);
            return bytes[0] |
                (bytes[1] << 8) |
                (bytes[2] << 16) |
                (bytes[3] << 24);
        }

        private static byte[] ReadExactly(
            BinaryReader reader,
            int count,
            string field)
        {
            if (count < 0 || count > MaximumDecodedBytes)
                throw new InvalidDataException(
                    "The " + field + " length is invalid.");

            byte[] result = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = reader.Read(
                    result, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException(
                        "Unexpected end of XNB while reading " +
                        field + ".");
                offset += read;
            }
            return result;
        }

        private static int CheckedPixelByteCount(
            int width,
            int height)
        {
            int count;
            try
            {
                count = checked(checked(width * height) * 4);
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException(
                    "The texture pixel count is too large.", ex);
            }

            if (count > MaximumDecodedBytes)
                throw new InvalidDataException(
                    "The texture pixel data is too large.");
            return count;
        }

        private static IntPtr Add(IntPtr pointer, int offset)
        {
            return new IntPtr(pointer.ToInt64() + offset);
        }

        private static string DescribeException(Exception exception)
        {
            if (exception == null)
                return "Unknown error.";
            while (exception is TargetInvocationException &&
                exception.InnerException != null)
            {
                exception = exception.InnerException;
            }
            return exception.GetType().Name + ": " +
                exception.Message;
        }

        private sealed class NonClosingReadStream : Stream
        {
            private readonly Stream _inner;

            public NonClosingReadStream(Stream inner)
            {
                if (inner == null)
                    throw new ArgumentNullException("inner");
                _inner = inner;
            }

            public override bool CanRead
            {
                get { return _inner.CanRead; }
            }

            public override bool CanSeek
            {
                get { return _inner.CanSeek; }
            }

            public override bool CanWrite
            {
                get { return _inner.CanWrite; }
            }

            public override long Length
            {
                get { return _inner.Length; }
            }

            public override long Position
            {
                get { return _inner.Position; }
                set { _inner.Position = value; }
            }

            public override void Flush()
            {
                _inner.Flush();
            }

            public override int Read(
                byte[] buffer,
                int offset,
                int count)
            {
                return _inner.Read(buffer, offset, count);
            }

            public override int ReadByte()
            {
                return _inner.ReadByte();
            }

            public override long Seek(
                long offset,
                SeekOrigin origin)
            {
                return _inner.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                _inner.SetLength(value);
            }

            public override void Write(
                byte[] buffer,
                int offset,
                int count)
            {
                _inner.Write(buffer, offset, count);
            }

            protected override void Dispose(bool disposing)
            {
                // XNA may dispose the wrapper while releasing its native LZX
                // context. The caller still owns the underlying input stream.
            }
        }
    }
}
