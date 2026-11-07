using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
internal static class GameInformation {
 internal static DateTime LastPlayed(string text){DateTime date;return DateTime.TryParse(text,out date)?date:DateTime.MinValue;}
 internal static string Version(string root) {
  try {
   var declared=GameUpdates.Read(root);if(declared!=null)return declared.Version;
  }catch{}
  try {
   foreach(string folder in new[]{Path.Combine(root,"csv"),root})foreach(string extension in new[]{"json","yml","yaml","csv"}) {
    string file=Path.Combine(folder,"GameBase."+extension);if(!File.Exists(file)||new FileInfo(file).Length>1048576)continue;
    var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    if(extension=="json") {
     var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));foreach(var pair in data)values[pair.Key]=Convert.ToString(pair.Value);
    } else foreach(string line in File.ReadAllLines(file)) {
     int split=line.IndexOf(extension=="csv"?',':':');if(split>0)values[line.Substring(0,split).Trim().Trim('"')]=line.Substring(split+1).Trim().Trim('"','\'');
    }
    foreach(string key in new[]{"versionName","版本名称","版本名","版本代号"})if(values.ContainsKey(key)&&values[key].Length>0)return values[key];
    foreach(string key in new[]{"version","版本"})if(values.ContainsKey(key)) {
     long number;if(long.TryParse(values[key],out number))return (number/1000.0).ToString("0.000",System.Globalization.CultureInfo.InvariantCulture);
     return values[key];
    }
   }
  } catch{}
  return "";
 }
}
