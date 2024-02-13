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


        // TODO Proper point interpolation for patches
        // TODO Proper texturing for patches
        // TODO Where are the damn surf ramps Lebowski


        static string entityMatcher = @"(?<entityName>(?:[-_\w\d:]|(?<!\/)\/)+)?\s*(?:\/\/[^\n]+\s*)*+(?<entityContent>\{(?:[^\{\}]++|(?R))*\})";
        //static string propsBrushMatcher = @"\{(?<properties>[^\{\}]+)(?<brushes>(?:\{(?:[^\{\}]+|(?R))*\}(?:[^\{\}]+))*)\s*\}";
        static string propsBrushMatcher = @"\{(?<properties>[^\{\}]+)(?<brushes>(?:\s\w+\s*\n\s+\{(?:[^\{\}]+|(?R))*\}(?:[^\{\}]+))*)\s*\}";
        static string brushesMatcher = @"(?<brushtype>\w+)\s*\n\s+(?<brush>\{(?:[^\{\}]+|(?R))*\})";

        static Regex uvaxisRegex = new Regex(@"\s*\[\s*([-\d\.\+E]+)\s+([-\d\.\+E]+)\s+([-\d\.\+E]+)\s+([-\d\.\+E]+)\s*\]\s*([-\d\.\+E]+)\s*", RegexOptions.IgnoreCase|RegexOptions.Compiled);


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
            StringBuilder ditched = new StringBuilder();

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

                Vector3? entityOffset = null;

                if (props.ContainsKey("origin"))
                {
                    entityOffset = parseVector3(props["origin"]);
                }

                bool isDetail = props["classname"].EndsWith("detail", StringComparison.InvariantCultureIgnoreCase);

                output.Append("{\n");
                foreach (var prop in props)
                {
                    output.Append($"\t\"{prop.Key}\" \"{prop.Value}\"\n");
                }

                if (string.IsNullOrWhiteSpace(brushes))
                {
                   // Console.WriteLine("Skipping {props} brush analysis, no brushes found.");
                    //continue;
                }

                var brushMatches = PcreRegex.Matches(brushes, brushesMatcher);

                foreach(var brush in brushMatches)
                {
                    bool displacementDetected = false;
                    StringBuilder brushText = new StringBuilder();
                    string brushType = brush.Groups["brushtype"].Value;
                    string brushData = brush.Groups["brush"].Value;

                    if(brushType != "solid")
                    {
                        Console.WriteLine($"Brush type '{brushType}' found, but not supported. Converting but may cause issues.");
                    }

                    List<Side> sides = new List<Side>(); // Need this for displacement


                    //output.Append("\t{\n\tbrushDef\n\t{\n");
                    brushText.Append("\t{\n");

                    var brushPropsSubGroupsMatch = PcreRegex.Match(brushData, propsBrushMatcher);

                    string brushPropsString = brushPropsSubGroupsMatch.Groups["properties"].Value;
                    string brushSubGroups = brushPropsSubGroupsMatch.Groups["brushes"].Value;

                    EntityProperties brushProps = EntityProperties.FromString(brushPropsString);

                    if (brushProps.ContainsKey("origin"))
                    {
                        Console.WriteLine("Brush itself contains origin? Huh.");
                    }

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
                        string sideContentstuff = sidePropsMatch.Groups["brushes"].Value; // Irrelevant

                        Displacement? dispInfo = null;

                        if( !string.IsNullOrWhiteSpace(sideContentstuff))
                        {
                            var sideContentMatches = PcreRegex.Matches(sideContentstuff, brushesMatcher);
                            foreach(var sideContentMatch in sideContentMatches)
                            {

                                string sideContentType = sideContentMatch.Groups["brushtype"].Value;
                                string sideContentContent = sideContentMatch.Groups["brush"].Value;
                                if (sideContentType.Equals("dispinfo", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    displacementDetected = true;

                                    dispInfo = new Displacement();
                                    var displacementPropsMatch = PcreRegex.Match(sideContentContent, propsBrushMatcher);
                                    string displacementPropsString = displacementPropsMatch.Groups["properties"].Value;
                                    EntityProperties displacementProps = EntityProperties.FromString(displacementPropsString);

                                    dispInfo.startposition = parseVector3(displacementProps["startposition"].Trim().Trim(new char[] { ']', '[' })).Value;

                                    if (entityOffset != null)
                                    {
                                        dispInfo.startposition -= entityOffset.Value;
                                    }

                                    dispInfo.power = int.Parse(displacementProps["power"]);

                                    string displacementContent = displacementPropsMatch.Groups["brushes"].Value; // Irrelevant
                                    var displacementContentMatches = PcreRegex.Matches(displacementContent, brushesMatcher);
                                    foreach (var displacementContentMatch in displacementContentMatches)
                                    {
                                        string displacementContentType = displacementContentMatch.Groups["brushtype"].Value;
                                        string displacementContentContent = displacementContentMatch.Groups["brush"].Value;

                                        var displacementContentPropsMatch = PcreRegex.Match(displacementContentContent, propsBrushMatcher);

                                        string displacementContentPropsString = displacementContentPropsMatch.Groups["properties"].Value;

                                        EntityProperties displacementContentProps = EntityProperties.FromString(displacementContentPropsString);

                                        if (displacementContentType.Equals("normals",StringComparison.InvariantCultureIgnoreCase))
                                        {
                                            List<Vector3[]> normals = new List<Vector3[]>();
                                            int row = 0;
                                            while (displacementContentProps.ContainsKey($"row{row}"))
                                            {
                                                string rowData = displacementContentProps[$"row{row}"];
                                                normals.Add(parseVector3Array(rowData));
                                                row++;
                                            }
                                            dispInfo.normals = normals.ToArray();
                                        }
                                        else if (displacementContentType.Equals("distances", StringComparison.InvariantCultureIgnoreCase))
                                        {
                                            List<double[]> distances = new List<double[]>();
                                            int row = 0;
                                            while (displacementContentProps.ContainsKey($"row{row}"))
                                            {
                                                string rowData = displacementContentProps[$"row{row}"];
                                                distances.Add(parseDoubleArray(rowData));
                                                row++;
                                            }
                                            dispInfo.distances = distances.ToArray();
                                        }
                                        else if (displacementContentType.Equals("alphas", StringComparison.InvariantCultureIgnoreCase))
                                        {
                                            List<double[]> alphas = new List<double[]>();
                                            int row = 0;
                                            while (displacementContentProps.ContainsKey($"row{row}"))
                                            {
                                                string rowData = displacementContentProps[$"row{row}"];
                                                alphas.Add(parseDoubleArray(rowData));
                                                row++;
                                            }
                                            dispInfo.alphas = alphas.ToArray();
                                        }
                                    }

                                }
                                else {
                                    Console.WriteLine($"Side content type {sideContentType} found. Ignoring.");
                                    ditched.Append(sideContentstuff);
                                }

                            }
                        }

                        EntityProperties sideProps = EntityProperties.FromString(sidePropsString);

                        brushText.Append("\t\t");

                        //if(entityOffset is null)
                        //{
                        //    brushText.Append(sideProps["plane"].Replace("(", "( ").Replace(")", " )"));
                        //} else
                        //{
                        Side thisSide = new Side();
                        Vector3[] vectors = parseVector3Array(sideProps["plane"]);
                        if(vectors.Length != 3)
                        {
                            Console.WriteLine($"plane consisted of {vectors.Length} vectors. Wtf. This will be ruined.");
                            brushText.Append(sideProps["plane"].Replace("(", "( ").Replace(")", " )"));
                        } else { 
                            
                            if(entityOffset != null)
                            {
                                vectors[0] -= entityOffset.Value;
                                vectors[1] -= entityOffset.Value;
                                vectors[2] -= entityOffset.Value;
                            }

                            thisSide.points = new Vector3[3] { vectors[0],vectors[1],vectors[2] };
                            if(dispInfo != null)
                            {
                                thisSide.dispinfo = dispInfo;
                            }

                            brushText.Append("( ");
                            brushText.Append(vectors[0].X.ToString("0.###"));
                            brushText.Append(" ");
                            brushText.Append(vectors[0].Y.ToString("0.###"));
                            brushText.Append(" ");
                            brushText.Append(vectors[0].Z.ToString("0.###"));
                            brushText.Append(" ");
                            brushText.Append(" ) ( ");
                            brushText.Append(vectors[1].X.ToString("0.###"));
                            brushText.Append(" ");
                            brushText.Append(vectors[1].Y.ToString("0.###"));
                            brushText.Append(" ");
                            brushText.Append(vectors[1].Z.ToString("0.###"));
                            brushText.Append(" ");
                            brushText.Append(" ) ( ");
                            brushText.Append(vectors[2].X.ToString("0.###"));
                            brushText.Append(" ");
                            brushText.Append(vectors[2].Y.ToString("0.###"));
                            brushText.Append(" ");
                            brushText.Append(vectors[2].Z.ToString("0.###"));
                            brushText.Append(" ) ");
                        }
                        //}


                        //brushText.Append(" (");
                        brushText.Append(" ");

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

                        thisSide.uAxisVector = uaxis;
                        thisSide.uAxisTranslation = uAxis4th;
                        thisSide.uAxisScale = uAxisScale;
                        thisSide.vAxisVector = vaxis;
                        thisSide.vAxisTranslation = vAxis4th;
                        thisSide.vAxisScale = vAxisScale;

                        //if(vAxis4th != 0.0f)
                        //{
                        //    Debug.WriteLine($"4th value of V axis not 0: {uAxis4th}. Idk what this even means but whatever.");
                        //}

                        /*
                        brushText.Append(" ( ");
                        brushText.Append(uaxis.X.ToString("0.###"));
                        brushText.Append(" ");
                        brushText.Append(uaxis.Y.ToString("0.###"));
                        brushText.Append(" ");
                        brushText.Append(uaxis.Z.ToString("0.###"));
                        brushText.Append(" ) ( ");
                        brushText.Append(vaxis.X.ToString("0.###"));
                        brushText.Append(" ");
                        brushText.Append(vaxis.Y.ToString("0.###"));
                        brushText.Append(" ");
                        brushText.Append(vaxis.Z.ToString("0.###"));

                        brushText.Append(" ) ) ");*/

                        string material = sideProps["material"];

                        if (material.StartsWith("tools/", StringComparison.InvariantCultureIgnoreCase))
                        {
                            string materialTmp = material.ToLower();
                            switch (materialTmp) {
                                case "tools/toolshint":
                                    material = "system/hint";
                                    break;
                                case "tools/toolsnodraw":
                                    material = "system/caulk";
                                    break;
                                case "tools/toolsplayerclip":
                                case "tools/toolsclip":
                                    material = "system/clip";
                                    break;
                                case "tools/toolstrigger":
                                case "tools/toolsteleport": // Not really official?
                                case "tools/toolstrigger_black": // Not really official?
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

                        thisSide.material = material;
                        sides.Add(thisSide);

                        brushText.Append(material);


                        brushText.Append(" [ ");
                        brushText.Append(uaxis.X.ToString("0.###"));
                        brushText.Append(" ");
                        brushText.Append(uaxis.Y.ToString("0.###"));
                        brushText.Append(" ");
                        brushText.Append(uaxis.Z.ToString("0.###"));
                        brushText.Append(" ");
                        brushText.Append(uAxis4th.ToString("0.###"));
                        brushText.Append(" ] [ ");
                        brushText.Append(vaxis.X.ToString("0.###"));
                        brushText.Append(" ");
                        brushText.Append(vaxis.Y.ToString("0.###"));
                        brushText.Append(" ");
                        brushText.Append(vaxis.Z.ToString("0.###"));
                        brushText.Append(" ");
                        brushText.Append(vAxis4th.ToString("0.###"));

                        brushText.Append(" ]  ");

                        brushText.Append(" 0 ");
                        brushText.Append(uAxisScale.ToString("0.###"));
                        brushText.Append(" ");
                        brushText.Append(vAxisScale.ToString("0.###"));
                        if (isDetail)
                        {
                            brushText.Append($" {detailFlag} 0 0");
                        }
                        brushText.Append("\n");

                        //brushText.Append(isDetail ? $" {detailFlag}" : " 0");
                        //brushText.Append(" 0 0\n");

                    }

                    //brushText.Append("\t}\n\t}\n");
                    brushText.Append("\t}\n");

                    if (displacementDetected)
                    {
                        //List<Vector3> verticies = new List<Vector3>();
                        Solid solid = new Solid();
                        solid.sides = sides.ToArray();

                        List<Side> completedSides = new List<Side>();
                        foreach(Side side in sides)
                        {
                            completedSides.Add(Side.completeSide(side, solid));
                        }

                        Solid finishedSolid = new Solid();
                        finishedSolid.sides = completedSides.ToArray();


                        foreach(Side side in finishedSolid.sides)
                        {
                            if (side.dispinfo == null) continue;


                            StringBuilder patchString = new StringBuilder();

                            patchString.Append("\n{\npatchDef2\n{");
                            patchString.Append($"\n{side.material}\n( {side.dispinfo.normals.Length} {side.dispinfo.normals[0].Length} 0 0 0 )\n(");

                            int startIndex = side.dispinfo.startposition.closestIndex(side.points);

                            Vector3[,] points = new Vector3[side.dispinfo.normals.Length, side.dispinfo.normals[0].Length]; 
                            Vector2[,] uvPoints = new Vector2[side.dispinfo.normals.Length, side.dispinfo.normals[0].Length]; 

                            //Get adjacent points by going around counter-clockwise
                            Vector3 a = side.points[MathExtensions.FloorMod((startIndex - 2), 4)];
                            Vector3 b = side.points[MathExtensions.FloorMod((startIndex - 1), 4)];
                            Vector3 c = side.points[startIndex];
                            Vector3 d = side.points[MathExtensions.FloorMod((startIndex + 1), 4)];
                            Vector3 cd = d-c;
                            Vector3 cb = b-c;
                            Vector3 ba = a-b;
                            // logger.log(Level.FINE, cd);
                            // logger.log(Level.FINE, cb);

                            int textureWidth = 512;
                            int textureHeight = 512; // Cant be helped

                            for (int i = 0; i < side.dispinfo.normals.Length; i++)
                            { // rows
                                for (int j = 0; j < side.dispinfo.normals[0].Length; j++)
                                { // columns
                                    double rowProgress = (double)j / (double)(side.dispinfo.normals[0].Length - 1);
                                    double colProgress = (double)i / (double)(side.dispinfo.normals.Length - 1);
                                    Vector3 point = side.points[startIndex]
                                            + (
                                                cd * (float)colProgress * (1.0f - (float)rowProgress) +
                                                ba * (float)colProgress * (float)rowProgress
                                            )
                                            + (cb * ((float)rowProgress))
                                            //+ (side.dispinfo.normals[i][j] * ((float)side.dispinfo.distances[i][j]));
                                            + (side.dispinfo.normals[j][i] * ((float)side.dispinfo.distances[j][i]));
                                    //verticies.Add(point);
                                    float u = Vector3.Dot(point, side.uAxisVector) / ((float)textureWidth * (float)side.uAxisScale)
                                        + (float)side.uAxisTranslation / (float)textureWidth;
                                    float v = Vector3.Dot(point, side.vAxisVector) / ((float)textureHeight * (float)side.vAxisScale)
                                            + (float)side.vAxisTranslation / (float)textureHeight;
                                    v = -v + (float)textureHeight;
                                    points[i, j] = point;
                                    uvPoints[i, j] = new Vector2() { X=u,Y=v };
                                }
                            }


                            for (int i = 0; i < side.dispinfo.normals.Length; i++)
                            { // rows
                                patchString.Append($"\n(");

                                for (int j = 0; j < side.dispinfo.normals[0].Length; j++)
                                { // columns

                                    Vector3 point = points[i, j];
                                    Vector2 uvPoint = uvPoints[i, j];
                                    patchString.Append($" ( ");
                                    patchString.Append(point.X.ToString("0.###"));
                                    patchString.Append(" ");
                                    patchString.Append(point.Y.ToString("0.###"));
                                    patchString.Append(" ");
                                    patchString.Append(point.Z.ToString("0.###"));
                                    patchString.Append($" {uvPoint.X} {uvPoint.Y} )");

                                }

                                patchString.Append($" )");
                            }




                            patchString.Append("\n)");

                            patchString.Append("\n}\n}"); 
                            
                            output.Append(patchString);
                        }


                    }

                    if (!displacementDetected)
                    {
                        output.Append(brushText);
                    }
                }

                //output.Append(brushes);
                output.Append("\n}\n");

            }

            File.WriteAllText($"{args[0]}.map",output.ToString());
            File.WriteAllText($"{args[0]}.ditched",ditched.ToString());
        }



        static Regex emptySpaceRegex = new Regex(@"\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static private Vector3? parseVector3(string colorString)
        {
            string prefilteredColor = emptySpaceRegex.Replace(colorString, " ");
            string[] components = prefilteredColor.Split(' ');

            if (components.Length < 3)
            {
                Trace.WriteLine("Vector3 with less than 3 components, skipping, weird.");
                return null;
            }

            Vector3 parsedColor = new Vector3();

            bool parseSuccess = true;
            parseSuccess = parseSuccess && float.TryParse(components[0], out parsedColor.X);
            parseSuccess = parseSuccess && float.TryParse(components[1], out parsedColor.Y);
            parseSuccess = parseSuccess && float.TryParse(components[2], out parsedColor.Z);

            if (!parseSuccess) return null;

            return parsedColor;
        }
        static private double[]? parseDoubleArray(string colorString)
        {
            string prefilteredColor = emptySpaceRegex.Replace(colorString, " ");
            string[] components = prefilteredColor.Split(' ');

            if (components.Length < 1)
            {
                Trace.WriteLine("Vector3 with less than 3 components, skipping, weird.");
                return null;
            }

            List<double> retVal = new List<double>();

            foreach (string component in components)
            {
                retVal.Add(double.Parse(component));
            }

            return retVal.ToArray();
        }

        static Regex numberVectorRegex = new Regex(@"([-\d\.\+E]+)\s+([-\d\.\+E]+)\s+([-\d\.\+E]+)",RegexOptions.Compiled|RegexOptions.IgnoreCase);

        static private Vector3[] parseVector3Array(string numbersString)
        {
            List<Vector3> retVal = new List<Vector3>();
            MatchCollection matches = numberVectorRegex.Matches(numbersString);
            foreach(Match match in matches)
            {
                Vector3? vector = parseVector3(match.Value);
                if(vector != null)
                {
                    retVal.Add(vector.Value);
                }
            }
            return retVal.ToArray();
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
