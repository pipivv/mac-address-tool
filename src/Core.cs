using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MacTool {
    public sealed class VendorBlock {
        public string Prefix, Vendor, Registry;
        public bool Selectable { get { return (Convert.ToByte(Prefix.Substring(0,2),16)&3)==0 && !Vendor.Equals("Private",StringComparison.OrdinalIgnoreCase) && Vendor.IndexOf("IEEE Registration Authority",StringComparison.OrdinalIgnoreCase)<0; } }
    }
    public sealed class MacDatabase {
        public const int DisplayLimit=100;
        public readonly List<VendorBlock> All = new List<VendorBlock>();
        public readonly Dictionary<string,List<VendorBlock>> Vendors = new Dictionary<string,List<VendorBlock>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Names;
        public readonly List<string> Common;
        readonly Dictionary<string,VendorBlock> byPrefix = new Dictionary<string,VendorBlock>(StringComparer.OrdinalIgnoreCase);
        public MacDatabase() {
            using(var raw=Assembly.GetExecutingAssembly().GetManifestResourceStream("vendors.tsv.gz"))
            using(var gz=new GZipStream(raw,CompressionMode.Decompress))
            using(var reader=new StreamReader(gz,Encoding.UTF8)) {
                string line;
                while((line=reader.ReadLine())!=null) {
                    var c=line.Split('\t');
                    if(c.Length!=3 || !Regex.IsMatch(c[0],"\\A(?:[0-9A-F]{6}|[0-9A-F]{7}|[0-9A-F]{9})\\z")) throw new InvalidDataException("厂商库记录无效");
                    var b=new VendorBlock{Prefix=c[0],Vendor=c[1],Registry=c[2]};
                    All.Add(b);
                }
            }
            // A few historical IEEE blocks have multiple named registrants. Retain that ambiguity.
            foreach(var group in All.GroupBy(x=>x.Prefix)) {
                var b=new VendorBlock{Prefix=group.Key,Registry=group.First().Registry,Vendor=string.Join(" / ",group.Select(x=>x.Vendor).Distinct(StringComparer.OrdinalIgnoreCase))};
                byPrefix[b.Prefix]=b;
                if(b.Selectable){if(!Vendors.ContainsKey(b.Vendor))Vendors[b.Vendor]=new List<VendorBlock>();Vendors[b.Vendor].Add(b);}
            }
            if(All.Count<40000 || Vendors.Count<10000) throw new InvalidDataException("内置厂商库不完整");
            Names=Vendors.Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();
            Common=new List<string>();
            foreach(string brand in new[]{"Intel","Apple","ASUSTek","Realtek","TP-Link","Huawei","Samsung","Xiaomi","Dell","Hewlett","Lenovo","Cisco","Tenda","ZTE"})
                foreach(string name in Search(brand).Take(2))if(!Common.Contains(name))Common.Add(name);
        }
        public VendorBlock Lookup(string input) {
            string m=Mac.Normalize(input); if(m==null)return null;
            VendorBlock b;
            foreach(int len in new[]{9,7,6}) if(byPrefix.TryGetValue(m.Substring(0,len),out b))return b;
            return null;
        }
        public List<string> Search(string text) {
            text=(text??"").Trim();
            if(text.Length==0)return new List<string>(Names);
            var aliases=new Dictionary<string,string>{{"苹果","Apple"},{"英特尔","Intel"},{"华为","Huawei"},{"小米","Xiaomi"},{"三星","Samsung"},{"联想","Lenovo"},{"戴尔","Dell"},{"惠普","Hewlett"},{"华硕","ASUSTek"},{"思科","Cisco"},{"瑞昱","Realtek"},{"中兴","ZTE"},{"水星","Mercury"},{"普联","TP-Link"},{"腾达","Tenda"}};
            bool alias=aliases.ContainsKey(text);if(alias)text=aliases[text];
            var word=new Regex("(?<![A-Za-z])"+Regex.Escape(text)+"(?![A-Za-z])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            var preferred=new List<string>();var partial=new List<string>();
            foreach(string name in Names){if(name.IndexOf(text,StringComparison.OrdinalIgnoreCase)<0)continue;bool exactWord=word.IsMatch(name);if(alias&&!exactWord)continue;if(exactWord)preferred.Add(name);else partial.Add(name);}
            preferred.AddRange(partial);return preferred;
        }
        public VendorQuery Query(string text,string selected){
            text=(text??"").Trim();var matches=text.Length==0?Names:Search(text);
            var visible=text.Length==0?new List<string>(Common):matches.Take(DisplayLimit).ToList();
            if(selected!=null&&!visible.Contains(selected)&&matches.Contains(selected)){if(visible.Count>=DisplayLimit)visible.RemoveAt(visible.Count-1);visible.Add(selected);}
            return new VendorQuery{Text=text,Matches=matches,Visible=visible};
        }
    }
    public sealed class VendorQuery {public string Text;public List<string> Matches,Visible;}
    public static class Mac {
        public static string Normalize(string input) {
            if(input==null)return null;
            string m=input.Trim();
            if(!Regex.IsMatch(m,"\\A(?:[0-9a-fA-F]{12}|(?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}|(?:[0-9a-fA-F]{2}-){5}[0-9a-fA-F]{2}|[0-9a-fA-F]{4}(?:\\.[0-9a-fA-F]{4}){2})\\z")) return null;
            return m.Replace(":","").Replace("-","").Replace(".","").ToUpperInvariant();
        }
        public static bool Valid(string input) { string m=Normalize(input); return m!=null && m!="000000000000" && m!="FFFFFFFFFFFF" && (Convert.ToByte(m.Substring(0,2),16)&1)==0; }
        public static string Format(string m) { m=Normalize(m);return m==null?"未提供":string.Join(":",Enumerable.Range(0,6).Select(i=>m.Substring(i*2,2))); }
        public static bool Local(string m) { m=Normalize(m);return m!=null && (Convert.ToByte(m.Substring(0,2),16)&2)!=0; }
    }
    public sealed class Generator : IDisposable {
        readonly RandomNumberGenerator rng=RandomNumberGenerator.Create();
        readonly MacDatabase db;
        public Generator(MacDatabase db){this.db=db;}
        public int Index(int count) {
            if(count<=0)throw new ArgumentOutOfRangeException("count");
            byte[] bytes=new byte[4]; uint n,limit=uint.MaxValue-(uint.MaxValue%(uint)count);
            do {rng.GetBytes(bytes); n=BitConverter.ToUInt32(bytes,0);}while(n>=limit);
            return (int)(n%(uint)count);
        }
        public string Generate(string vendor,bool local,HashSet<string> excluded,out VendorBlock block) {
            block=null;
            if(!local && (vendor==null || !db.Vendors.ContainsKey(vendor)))throw new ArgumentException("请选择有效厂商");
            for(int trial=0;trial<2000;trial++) {
                byte[] bytes=new byte[6];rng.GetBytes(bytes);
                string m;
                if(local) { bytes[0]=(byte)((bytes[0]&0xFC)|2);m=BitConverter.ToString(bytes).Replace("-",""); }
                else {
                    var blocks=db.Vendors[vendor]; var chosen=blocks[Index(blocks.Count)];
                    string random=BitConverter.ToString(bytes).Replace("-","");
                    m=chosen.Prefix+random.Substring(chosen.Prefix.Length);
                    var matched=db.Lookup(m);
                    if(matched==null || !matched.Vendor.Equals(vendor,StringComparison.OrdinalIgnoreCase))continue;
                    block=matched;
                }
                if(Mac.Valid(m) && (excluded==null || !excluded.Contains(m)))return m;
            }
            throw new InvalidOperationException("该厂商当前可用地址不足，请选择其他厂商："+vendor);
        }
        public void Dispose(){rng.Dispose();}
    }
}
