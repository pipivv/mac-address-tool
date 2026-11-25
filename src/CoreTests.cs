using System;
using System.Collections.Generic;
using System.Linq;
namespace MacTool {
 public static class CoreTests {
  public static object Run(){
   var database=new MacDatabase();int assertions=0;Action<bool,string> check=(value,message)=>{assertions++;if(!value)throw new Exception(message);};
   check(database.All.Count>50000,"Registry completeness");check(database.Names.Count>30000,"Vendor completeness");
   check(!Mac.Valid("000000000000"),"Reject zero");check(!Mac.Valid("FFFFFFFFFFFF"),"Reject broadcast");check(!Mac.Valid("01:00:00:00:00:01"),"Reject multicast");check(!Mac.Valid("020000000001;echo"),"Reject malformed input");
   check(Mac.Normalize("02:00:00:00:00:01")=="020000000001","Normalize demo address");
   var unique=new HashSet<string>();
   using(var generator=new Generator(database)){
    foreach(string vendor in database.Names){VendorBlock block;string address=generator.Generate(vendor,false,unique,out block);check(Mac.Valid(address)&&!Mac.Local(address),"Valid global format");check(database.Lookup(address).Vendor.Equals(vendor,StringComparison.OrdinalIgnoreCase),"Vendor lookup matches");check(unique.Add(address),"Distinct samples");}
    for(int i=0;i<2000;i++){VendorBlock block;string address=generator.Generate(null,true,unique,out block);check(Mac.Valid(address)&&Mac.Local(address),"Local unicast format");check(unique.Add(address),"Distinct local samples");}
   }
   var common=database.Query("",null);check(common.Visible.Count<=50&&common.Matches.Count==database.Names.Count,"Small initial list, full random pool");
   var broad=database.Query("a",null);check(broad.Visible.Count<=100&&broad.Matches.Count>100,"Search display limit");
   check(database.Search("苹果").Any(v=>v.Contains("Apple")),"Chinese alias");
   return new{Result="PASS",Assertions=assertions,RegistryRows=database.All.Count,VendorNames=database.Names.Count,AdditionalLocalSamples=2000,NetworkSettingsChanged=false};
  }
 }
}
