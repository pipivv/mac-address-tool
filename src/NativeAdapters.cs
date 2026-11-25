using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.ComponentModel;
namespace MacTool {
    public static class NativeAdapters {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct IfRow {
            public ulong Luid;public uint Index;public Guid Guid;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=257)]public string Alias;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=257)]public string Description;
            public uint AddressLength;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)]public byte[] Physical;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)]public byte[] Permanent;
            public uint Mtu,Type,TunnelType,MediaType,PhysicalMediumType,AccessType,DirectionType;
            public byte Flags;public uint OperStatus,AdminStatus,MediaConnectState;public Guid NetworkGuid;public uint ConnectionType;
            public ulong TransmitLinkSpeed,ReceiveLinkSpeed,InOctets,InUcastPkts,InNUcastPkts,InDiscards,InErrors,InUnknownProtos,InUcastOctets,InMulticastOctets,InBroadcastOctets,OutOctets,OutUcastPkts,OutNUcastPkts,OutDiscards,OutErrors,OutUcastOctets,OutMulticastOctets,OutBroadcastOctets,OutQLen;
        }
        [DllImport("iphlpapi.dll")]static extern uint ConvertInterfaceGuidToLuid(ref Guid guid,out ulong luid);
        [DllImport("iphlpapi.dll")]static extern uint GetIfEntry2(ref IfRow row);
        [DllImport("iphlpapi.dll")]static extern uint GetIfTable2(out IntPtr table);
        [DllImport("iphlpapi.dll")]static extern void FreeMibTable(IntPtr table);
        [StructLayout(LayoutKind.Sequential)]struct TableHeader{public uint Count;public IfRow First;}
        public static List<Adapter> ReadAll(){
            IntPtr table;uint status=GetIfTable2(out table);if(status!=0)throw new Win32Exception((int)status);
            try{
                int count=Marshal.ReadInt32(table);if(count<0||count>100000)throw new InvalidOperationException("系统网卡数量异常。");
                int offset=Marshal.OffsetOf(typeof(TableHeader),"First").ToInt32(),stride=Marshal.SizeOf(typeof(IfRow));var list=new List<Adapter>();
                for(int i=0;i<count;i++){
                    var row=(IfRow)Marshal.PtrToStructure(IntPtr.Add(table,offset+i*stride),typeof(IfRow));
                    if(row.Guid==Guid.Empty||row.AddressLength!=6||row.Type==24)continue;
                    var a=new Adapter{Id=row.Guid.ToString("D"),Name=row.Alias,Description=row.Description};Fill(a,row);list.Add(a);
                }
                return list;
            }finally{FreeMibTable(table);}
        }
        static void Fill(Adapter a,IfRow row){
            string current=BitConverter.ToString(row.Physical.Take(6).ToArray()).Replace("-","");
            string permanent=BitConverter.ToString(row.Permanent.Take(6).ToArray()).Replace("-","");
            if(current!="000000000000")a.Current=current;if(permanent!="000000000000")a.Permanent=permanent;
            a.Physical=(row.Flags&1)!=0;a.Wireless=row.Type==71||row.PhysicalMediumType==9||row.PhysicalMediumType==1;
            a.Disabled=row.AdminStatus!=1;a.Status=a.Disabled?"Disabled":row.OperStatus==1?"Up":"Disconnected";
        }
        public static bool Enrich(Adapter a) {
            Guid guid=Guid.Parse(a.Id);ulong luid;
            if(ConvertInterfaceGuidToLuid(ref guid,out luid)!=0)return false;
            var row=new IfRow{Luid=luid,Alias="",Description="",Physical=new byte[32],Permanent=new byte[32]};
            if(GetIfEntry2(ref row)!=0||row.Guid!=guid||row.AddressLength!=6)return false;
            Fill(a,row);
            return true;
        }
    }
}
