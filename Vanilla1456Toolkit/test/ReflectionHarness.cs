using System;using System.IO;using System.Reflection;using System.Collections.Generic;
[assembly:AssemblyVersion("1.4.5.6")]
namespace Terraria1456Toolkit {
 internal sealed class InventoryEntry { public int Slot,Id,Stack,Prefix;public string Name; }
 static class Test { static void Main(){Terraria.Main.player=new Terraria.Player[]{new Terraria.Player{active=true,name="me",whoAmI=0,statLife=1,statLifeMax2=500,statManaMax2=200,breathMax=200,inventory=new Terraria.Item[]{new Terraria.Item()}}};var api=VanillaGameApi.Connect();api.ApplyCheats(true,false,false,false,false,false,false,false,false,false,false);api.ReplaceSlot(0,42,9);var inv=api.ReadInventory();if(Terraria.Main.player[0].statLife==500&&inv[0].Id==42&&inv[0].Stack==9)File.WriteAllText("reflection_ok.txt","ok");} }
}
namespace Terraria {
 public static class Main { public static Player[] player;public static int myPlayer=0,netMode=0;public static bool[] debuff=new bool[100];}
 public class Player {public bool active,creativeGodMode,immune,immuneNoBlink,noKnockback,noFallDmg,lavaImmune;public int whoAmI,immuneTime,statLife,statLifeMax2,statMana,statManaMax2,breath,breathMax,potionDelay,wingTimeMax,rocketTime,rocketTimeMax;public float moveSpeed,maxRunSpeed,accRunSpeed,runAcceleration,jumpSpeedBoost,wingTime;public int[] buffType=new int[10];public Item[] inventory;public string name;public Item HeldItem {get{return inventory[0];}} public void DelBuff(int i){} public object GetItemSource_Misc(int x){return new object();} public void QuickSpawnItem(object s,int t,int n){} }
 public class Item {public int type,stack,maxStack=9999;public byte prefix;public string Name {get{return "item"+type;}} public bool IsAir {get{return type==0;}} public void SetDefaults(int t,bool noMatCheck=false){type=t;maxStack=9999;}public void TurnToAir(){type=0;stack=0;}}
 public static class NetMessage {public static void SendData(int a,int b=-1,int c=-1,object d=null,int e=0,float f=0,float g=0,float h=0,int i=0,int j=0,int k=0){} }
}
