using PCRE;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace VMF2MAP
{
    class Program
    {

        static string entityMatcher = @"(?<entityName>(?:[-_\w\d:]|(?<!\/)\/)+)?\s*(?:\/\/[^\n]+\s*)*+(?<entityContent>\{(?:[^\{\}]+|(?R))*\})";
        //static string propsBrushMatcher = @"\{(?<properties>[^\{\}]+)(?<brushes>(?:\{(?:[^\{\}]+|(?R))*\}(?:[^\{\}]+))*)\s*\}";
        static string propsBrushMatcher = @"\{(?<properties>[^\{\}]+)(?<brushes>(?:\s\w+\s*\n\s+\{(?:[^\{\}]+|(?R))*\}(?:[^\{\}]+))*)\s*\}";
        static string brushesMatcher = @"(?<brushtype>\w+)\s*\n\s+(?<brush>\{(?:[^\{\}]+|(?R))*\})";

        static Regex uvaxisRegex = new Regex(@"\s*\[\s*([-\d\.\+E]+)\s*([-\d\.\+E]+)\s*([-\d\.\+E]+)\s*([-\d\.\+E]+)\s*\]\s*([-\d\.\+E]+)\s*", RegexOptions.IgnoreCase|RegexOptions.Compiled);


        const int detailFlag = 0x8000000;

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                return;
            }

            string vmfText = File.ReadAllText(args[0]);

            var entities = PcreRegex.Matches(vmfText, entityMatcher);

            StringBuilder output = new StringBuilder();

            foreach(var entity in entities)
            {
                string entType = entity.Groups["entityName"].Value;
                var propsBrushMatch = PcreRegex.Match(entity.Groups["entityContent"].Value, propsBrushMatcher);
                string properties = propsBrushMatch.Groups["properties"].Value;
                string brushes = propsBrushMatch.Groups["brushes"].Value;

                if (entType != "world" && entType != "entity")
                {
                    Console.WriteLine($"Entity type '{entType}' found, but not supported. Skipping.");
                    continue;
                }

                EntityProperties props = EntityProperties.FromString(properties);

                bool isDetail = props["classname"].EndsWith("detail", StringComparison.InvariantCultureIgnoreCase);

                output.Append("{\n");
                foreach (var prop in props)
                {
                    output.Append($"\t\"{prop.Key}\" \"{prop.Value}\"\n");
                }

                if (string.IsNullOrWhiteSpace(brushes))
                {
                   // Console.WriteLine("Skipping {props} brush analysis, no brushes found.");
                    continue;
                }

                var brushMatches = PcreRegex.Matches(brushes, brushesMatcher);

                foreach(var brush in brushMatches)
                {
                    string brushType = brush.Groups["brushtype"].Value;
                    string brushData = brush.Groups["brush"].Value;

                    if(brushType != "solid")
                    {
                        Console.WriteLine($"Brush type '{brushType}' found, but not supported. Converting but may cause issues.");
                    }


                    //output.Append("\t{\n\tbrushDef\n\t{\n");
                    output.Append("\t{\n");

                    var brushPropsSubGroupsMatch = PcreRegex.Match(brushData, propsBrushMatcher);

                    string brushProps = brushPropsSubGroupsMatch.Groups["properties"].Value;
                    string brushSubGroups = brushPropsSubGroupsMatch.Groups["brushes"].Value;

                    // Subgroup are sides.
                    var subGroupMatches = PcreRegex.Matches(brushSubGroups, brushesMatcher);
                    foreach (var side in subGroupMatches)
                    {
                        string sideType = side.Groups["brushtype"].Value;
                        string sideContent = side.Groups["brush"].Value;

                        if (sideType != "side")
                        {
                            Console.WriteLine($"Side type '{brushType}' found, but not supported. Converting but may cause issues.");
                        }

                        var sidePropsMatch = PcreRegex.Match(sideContent, propsBrushMatcher);

                        string sidePropsString = sidePropsMatch.Groups["properties"].Value;
                        //string sideContentstuff = sidePropsMatch.Groups["brushes"].Value; // Irrelevant


                        EntityProperties sideProps = EntityProperties.FromString(sidePropsString);

                        output.Append("\t\t");
                        output.Append(sideProps["plane"].Replace("(","( ").Replace(")"," )"));
                        //output.Append(" (");
                        output.Append(" ");

                        Vector3 uaxis = new Vector3();
                        Vector3 vaxis = new Vector3();

                        Match uAxisResult = uvaxisRegex.Match(sideProps["uaxis"]);
                        Match vAxisResult = uvaxisRegex.Match(sideProps["vaxis"]);

                        uaxis.X = float.Parse(uAxisResult.Groups[1].Value);
                        uaxis.Y = float.Parse(uAxisResult.Groups[2].Value);
                        uaxis.Z = float.Parse(uAxisResult.Groups[3].Value);
                        float uAxis4th = float.Parse(uAxisResult.Groups[4].Value);
                        float uAxisScale = float.Parse(uAxisResult.Groups[5].Value);
                        //uaxis *= uAxisScale;

                        //if(uAxis4th != 0.0f)
                        //{
                        //    Debug.WriteLine($"4th value of U axis not 0: {uAxis4th}. Idk what this even means but whatever.");
                        //}

                        vaxis.X = float.Parse(vAxisResult.Groups[1].Value);
                        vaxis.Y = float.Parse(vAxisResult.Groups[2].Value);
                        vaxis.Z = float.Parse(vAxisResult.Groups[3].Value);
                        float vAxis4th = float.Parse(vAxisResult.Groups[4].Value);
                        float vAxisScale = float.Parse(vAxisResult.Groups[5].Value);
                        //vaxis *= vAxisScale;

                        //if(vAxis4th != 0.0f)
                        //{
                        //    Debug.WriteLine($"4th value of V axis not 0: {uAxis4th}. Idk what this even means but whatever.");
                        //}

                        /*
                        output.Append(" ( ");
                        output.Append(uaxis.X.ToString("0.###"));
                        output.Append(" ");
                        output.Append(uaxis.Y.ToString("0.###"));
                        output.Append(" ");
                        output.Append(uaxis.Z.ToString("0.###"));
                        output.Append(" ) ( ");
                        output.Append(vaxis.X.ToString("0.###"));
                        output.Append(" ");
                        output.Append(vaxis.Y.ToString("0.###"));
                        output.Append(" ");
                        output.Append(vaxis.Z.ToString("0.###"));

                        output.Append(" ) ) ");*/

                        string material = sideProps["material"];

                        if (material.StartsWith("tools/", StringComparison.InvariantCultureIgnoreCase))
                        {
                            string materialTmp = material.ToLower();
                            switch (materialTmp) {
                                case "tools/toolshint":
                                    material = "system/hint";
                                    break;
                                case "tools/toolsnodraw":
                                    material = "system/nodraw";
                                    break;
                                case "tools/toolsplayerclip":
                                case "tools/toolsclip":
                                    material = "system/clip";
                                    break;
                                case "tools/toolstrigger":
                                    material = "system/trigger";
                                    break;
                                case "tools/toolsinvisible":
                                case "tools/toolsskip":
                                    material = "system/skip";
                                    break;
                                case "tools/toolsareaportal":
                                    material = "system/areaportal";
                                    break;
                                case "tools/toolsblocklight":
                                    //material = "system/caulk";
                                    break;
                                case "tools/toolsskybox":
                                    //material = "system/caulk";
                                    break;
                                default:
                                    Console.WriteLine($"Unknown tools type {materialTmp}");
                                    break;
                            }

                        }

                        output.Append(material);


                        output.Append(" [ ");
                        output.Append(uaxis.X.ToString("0.###"));
                        output.Append(" ");
                        output.Append(uaxis.Y.ToString("0.###"));
                        output.Append(" ");
                        output.Append(uaxis.Z.ToString("0.###"));
                        output.Append(" ");
                        output.Append(uAxis4th.ToString("0.###"));
                        output.Append(" ] [ ");
                        output.Append(vaxis.X.ToString("0.###"));
                        output.Append(" ");
                        output.Append(vaxis.Y.ToString("0.###"));
                        output.Append(" ");
                        output.Append(vaxis.Z.ToString("0.###"));
                        output.Append(" ");
                        output.Append(vAxis4th.ToString("0.###"));

                        output.Append(" ]  ");

                        output.Append(" 0 ");
                        output.Append(uAxisScale.ToString("0.###"));
                        output.Append(" ");
                        output.Append(vAxisScale.ToString("0.###"));
                        if (isDetail)
                        {
                            output.Append($" {detailFlag}");
                        }
                        output.Append("\n");

                        //output.Append(isDetail ? $" {detailFlag}" : " 0");
                        //output.Append(" 0 0\n");

                    }

                    //output.Append("\t}\n\t}\n");
                    output.Append("\t}\n");
                }

                //output.Append(brushes);
                output.Append("}\n");

            }

            File.WriteAllText($"{args[0]}.map",output.ToString());
        }
    }


    public class EntityProperties : Dictionary<string, string>, INotifyPropertyChanged
    {
        public EntityProperties() : base(StringComparer.InvariantCultureIgnoreCase)
        {
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (var kvp in this)
            {
                sb.Append($"\"{(kvp.Key + '\"').PadRight(10)} \"{kvp.Value}\"\n");
            }
            return sb.ToString();
        }

        static Regex singleEntityParseRegex = new Regex(@"(\s*""([^""]+)""[ \t]+""([^""]+)"")+\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        public static EntityProperties FromString(string propertiesString)
        {
            MatchCollection matches = singleEntityParseRegex.Matches(propertiesString);
            foreach (Match match in matches)
            {
                if (match.Groups.Count >= 4)
                {
                    EntityProperties props = new EntityProperties();

                    int lineCount = match.Groups[2].Captures.Count;
                    for (int c = 0; c < lineCount; c++)
                    {
                        //Trace.WriteLine($"{match.Groups[2].Captures[c].Value}:{match.Groups[3].Captures[c].Value}");
                        props[match.Groups[2].Captures[c].Value] = match.Groups[3].Captures[c].Value;
                    }
                    return props;
                }
            }
            return null;
        }

        public string String => this.ToString();


        public override bool Equals(object obj)
        {
            EntityProperties other = obj as EntityProperties;
            if (!(other is null))
            {
                if (this.Count == other.Count)
                {
                    foreach (var kvp in this)
                    {
                        if (!other.ContainsKey(kvp.Key) || other[kvp.Key] != kvp.Value)
                        {
                            return false;
                        }
                    }
                    return true;
                }
            }
            return false;
        }

        public override int GetHashCode()
        {
            int hash = 0;
            bool firstDone = false;

            string[] keys = this.Keys.ToArray();
            Array.Sort(keys, StringComparer.InvariantCultureIgnoreCase);

            foreach (var key in keys)
            {

                int hereHash = HashCode.Combine(key.GetHashCode(StringComparison.InvariantCultureIgnoreCase), this[key].GetHashCode(StringComparison.InvariantCultureIgnoreCase));
                if (!firstDone)
                {
                    hash = hereHash;
                }
                else
                {
                    hash = HashCode.Combine(hereHash, hash);
                    firstDone = true;
                }
            }
            return hash;
        }
    }
}
