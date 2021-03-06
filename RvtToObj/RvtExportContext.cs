﻿using System;
using Autodesk.Revit.DB;
using System.Diagnostics;
using System.IO;
#if R2016
using Autodesk.Revit.Utility;
#elif R2018
using Autodesk.Revit.DB.Visual;
#endif
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace RvtToObj
{
    internal class RvtExportContext : IExportContext
    {

        #region mtl statement format strings
        const string _abstract
            = "RVT TO OBJ    Created Time:{0}";
        const string _mtl_newmtl_d
            = "\r\nnewmtl {0}\r\n"
            + "ka {1} {2} {3}\r\n"
            + "Kd {1} {2} {3}\r\n"
            + "Ns {4}\r\n"
            + "d {5}\r\n";
        const string _obj_mtllib = "mtllib {0}";
        const string _obj_vertex = "v {0} {1} {2}";
        const string _obj_normal = "vn {0} {1} {2}";
        const string _obj_uv = "vt {0} {1}";
        const string _obj_usemtl = "usemtl {0}";
        const string _obj_face = "f {0}/{3}/{6} {1}/{4}/{7} {2}/{5}/{8}";
        const string _mtl_bitmap = "map_Kd {0}";
        #endregion

        #region VertexLookupXyz
        /// <summary>
        /// 去除重复顶点的查找类
        /// </summary>
        class VertexLookupXyz : Dictionary<XYZ, int>
        {
            #region XyzEqualityComparer
            /// <summary>
            /// 为XYZ类型的点定义比较类，引入一个松弛常量
            /// </summary>
            class XyzEqualityComparer : IEqualityComparer<XYZ>
            {
                const double _sixteenthInchInFeet
                  = 1.0 / (16.0 * 12.0);

                public bool Equals(XYZ p, XYZ q)
                {
                    return p.IsAlmostEqualTo(q,
                      _sixteenthInchInFeet);
                }

                public int GetHashCode(XYZ p)
                {
                    return Util.PointString(p).GetHashCode();
                }
            }
            #endregion // XyzEqualityComparer

            public VertexLookupXyz()
              : base(new XyzEqualityComparer())
            {
            }

            /// <summary>
            /// 添加未包含点的索引并返回
            /// </summary>
            public int AddVertex(XYZ p)
            {
                return ContainsKey(p)
                  ? this[p]
                  : this[p] = Count;
            }
        }
        #endregion // VertexLookupXyz

        #region VertexLookupInt
        class PointDouble : IComparable<PointDouble>
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }

            const double _eps = 1.0e-9;

            public PointDouble(UV p)
            {
                X = p.U;
                Y = p.V;
            }
            public PointDouble(XYZ p, bool switch_coordinates)
            {
                X = p.X;
                Y = p.Y;
                Z = p.Z;

                if (switch_coordinates)
                {
                    X = X;
                    double tmp = Y;
                    Y = Z;
                    Z = -tmp;
                }
            }

            private static int CompareDouble(double a, double b)
            {
                if (Math.Abs(a - b) < _eps) return 0;
                if (a > b) return 1;
                return -1;
            }

            public int CompareTo(PointDouble a)
            {
                var d = CompareDouble(X, a.X);
                if (d != 0) return d;
                d = CompareDouble(Y, a.Y);
                if (d != 0) return d;
                return CompareDouble(Z, a.Z);

            }
        }

        class PointInt : IComparable<PointInt>, IEquatable<PointInt>
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }

            const double _eps = 1.0e-9;

            const double _feet_to_mm = 25.4 * 12;

            static double ConvertFeetToMillimetres(double d)
            {
                return _feet_to_mm * d;
            }

            public PointInt(XYZ p, bool switch_coordinates)
            {
                X = ConvertFeetToMillimetres(p.X);
                Y = ConvertFeetToMillimetres(p.Y);
                Z = ConvertFeetToMillimetres(p.Z);

                if (switch_coordinates)
                {
                    X = X;
                    var tmp = Y;
                    Y = Z;
                    Z = -tmp;
                }
            }

            private static int CompareDouble(double a, double b)
            {
                if (Math.Abs(a - b) < _eps) return 0;
                if (a > b) return 1;
                return -1;
            }
            public int CompareTo(PointInt a)
            {
                var d = CompareDouble(X, a.X);
                if (d != 0) return d;
                d = CompareDouble(Y, a.Y);
                if (d != 0) return d;
                return CompareDouble(Z, a.Z);
            }

            public bool Equals(PointInt other)
            {
                if (other == null) return false;
                return Math.Abs(X - other.X) < _eps && Math.Abs(Y - other.Y) < _eps && Math.Abs(Z - other.Z) < _eps;
            }

            public override bool Equals(object obj)
            {
                var o = obj as PointInt;
                return Equals(o);
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

        }

        class VertexLookupDouble : Dictionary<PointDouble, int>
        {
            #region PointIntEqualityComparer

            class PointDoubleEqualityComparer : IEqualityComparer<PointDouble>
            {
                public bool Equals(PointDouble p, PointDouble q)
                {
                    return 0 == p.CompareTo(q);
                }

                public int GetHashCode(PointDouble p)
                {
                    var format = "{0.#########}";
                    return (p.X.ToString(format)
                      + "," + p.Y.ToString(format)
                      + "," + p.Z.ToString(format))
                      .GetHashCode();
                }
            }
            #endregion // PointIntEqualityComparer

            public VertexLookupDouble()
              : base(new PointDoubleEqualityComparer())
            {
            }

            public int AddVertex(PointDouble p)
            {
                return ContainsKey(p)
                  ? this[p]
                  : this[p] = Count;
            }
        }

        class VertexLookupInt : Dictionary<PointInt, int>
        {
            #region PointIntEqualityComparer

            class PointIntEqualityComparer : IEqualityComparer<PointInt>
            {
                public bool Equals(PointInt p, PointInt q)
                {
                    return 0 == p.CompareTo(q);
                }

                public int GetHashCode(PointInt p)
                {
                    return (p.X.ToString()
                      + "," + p.Y.ToString()
                      + "," + p.Z.ToString())
                      .GetHashCode();
                }
            }
            #endregion // PointIntEqualityComparer

            public VertexLookupInt()
              : base(new PointIntEqualityComparer())
            {
            }

            public int AddVertex(PointInt p)
            {
                return ContainsKey(p)
                  ? this[p]
                  : this[p] = Count;
            }
        }
        #endregion // VertexLookupInt


        //材质信息
        Color currentColor;
        int currentTransparencyint;
        double currentTransparencyDouble;
        int currentShiniess;
        string ttrgb=string.Empty;

        int materialIndex = 0;
        ElementId currentMterialId = ElementId.InvalidElementId;
        List<string> map = new List<string>();
        Dictionary<string, Color> colors = new Dictionary<string, Color>();
        Dictionary<string, double> transparencys = new Dictionary<string, double>();
        Dictionary<string, int> shiniess = new Dictionary<string, int>();
        Dictionary<string, string> texture = new Dictionary<string, string>();

        //几何信息
        List<int> face = new List<int>();
        VertexLookupInt _vertices = new VertexLookupInt();
        VertexLookupDouble _uvs = new VertexLookupDouble();
        VertexLookupDouble _normals = new VertexLookupDouble();

        private static string TextureFolder = null;
        bool _switch_coordinates = true;
        Document _doc;
        string _filename;
        AssetSet _objlibraryAsset;
        Stack<Transform> _transformationStack = new Stack<Transform>();

        Transform CurrentTransform
        {
            get
            {
                return _transformationStack.Peek();
            }
        }

        public RvtExportContext(Document doc, string filename, AssetSet objlibraryAsset)
        {
            this._doc = doc;
            this._filename = filename;
            this._objlibraryAsset = objlibraryAsset;
        }

        /// <summary>
        /// 读取Asset
        /// </summary>
        /// <param name="asset"></param>
        /// <param name="ttrgb"></param>
        public void ReadAsset(Asset asset,string ttrgb)
        {
            // 遍历Asset中的各个属性.
            for (int idx = 0; idx < asset.Size; idx++)
            {
                AssetProperty prop = asset[idx];
                ReadAssetProperty(prop, ttrgb);
            }
        }

        /// <summary>
        /// 读取Asset中的各种属性
        /// </summary>
        /// <param name="prop"></param>
        /// <param name="ttrgb"></param>
        public void ReadAssetProperty(AssetProperty prop, string ttrgb)
        {
            switch (prop.Type)
            { 
#if R2016
                case AssetPropertyType.APT_Integer:
#elif R2018
                case AssetPropertyType.Integer:
#endif
                    var AssetPropertyInt = prop as AssetPropertyInteger;                  
                    break;
#if R2016
                case AssetPropertyType.APT_Distance:
#elif R2018
                case AssetPropertyType.Distance:
#endif
                    var AssetPropertyDistance = prop as AssetPropertyDistance;
                    break;
#if R2016
                case AssetPropertyType.APT_Double:
#elif R2018
                case AssetPropertyType.Double1:
#endif
                    var AssetPropertyDouble = prop as AssetPropertyDouble;
                    break;
#if R2016
                case AssetPropertyType.APT_DoubleArray2d:
#elif R2018
                case AssetPropertyType.Double2:
#endif
                    var AssetPropertyDoubleArray2d = prop as AssetPropertyDoubleArray2d;
                    break;
#if R2016
                case AssetPropertyType.APT_DoubleArray4d:
#elif R2018
                case AssetPropertyType.Double4:
#endif
                    var AssetPropertyDoubleArray4d = prop as AssetPropertyDoubleArray4d;
                    break;
#if R2016
                case AssetPropertyType.APT_String:
#elif R2018
                case AssetPropertyType.String:
#endif
                    AssetPropertyString val = prop as AssetPropertyString;
                    if (val.Name == "unifiedbitmap_Bitmap" && val.Value != "")
                    {
                        map.Add(ttrgb);
                        map.Add(val.Value.Trim().Replace("\\", ""));
                    }

                    break;

#if R2016
                case AssetPropertyType.APT_Boolean:
#elif R2018
                case AssetPropertyType.Boolean:
#endif
                    AssetPropertyBoolean boolProp = prop as AssetPropertyBoolean;
                    break;

#if R2016
                case AssetPropertyType.APT_Double44:
#elif R2018
                case AssetPropertyType.Double44:
#endif
                    AssetPropertyDoubleArray4d transformProp = prop as AssetPropertyDoubleArray4d;
#if R2016
                    DoubleArray tranformValue = transformProp.Value;
#elif R2018
                    DoubleArray tranformValue = (DoubleArray)transformProp.GetValueAsDoubles();
#endif
                    break;
                //APT_Lis包含了一系列的子属性值 
#if R2016
                case AssetPropertyType.APT_List:
#elif R2018
                case AssetPropertyType.List:
#endif
                    AssetPropertyList propList = prop as AssetPropertyList;
                    IList<AssetProperty> subProps = propList.GetValue();
                    if (subProps.Count == 0)
                        break;
                    switch (subProps[0].Type)
                    {
#if R2016
                        case AssetPropertyType.APT_Integer:
#elif R2018
                        case AssetPropertyType.Integer:
#endif
                            foreach (AssetProperty subProp in subProps)
                            {
                                AssetPropertyInteger intProp = subProp as AssetPropertyInteger;
                                int intValue = intProp.Value;
                            }
                            break;
#if R2016
                        case AssetPropertyType.APT_String:
#elif R2018
                        case AssetPropertyType.String:
#endif

                            foreach (AssetProperty subProp in subProps)
                            {
                                AssetPropertyString intProp = subProp as AssetPropertyString;
                                string intValue = intProp.Value;
                                if (intProp.Name == "unifiedbitmap_Bitmap" && intProp.Value != "")
                                {
                                    map.Add(ttrgb);
                                    map.Add(intProp.Value.Trim().Replace("\\", ""));
                                }
                            }
                            break;
                    }
                    break;
#if R2016
                case AssetPropertyType.APT_Asset:
#elif R2018
                case AssetPropertyType.Asset:
#endif
                    Asset propAsset = prop as Asset;
                    for (int i = 0; i < propAsset.Size; i++)
                    {
                        ReadAssetProperty(propAsset[i], ttrgb);
                    }
                    break;
#if R2016
                case AssetPropertyType.APT_Reference:
#elif R2018
                case AssetPropertyType.Reference:
#endif
                    break;
                default:
                    break;
            }

            //遍历连接属性，一般位图信息存储在这里  
            if (prop.NumberOfConnectedProperties == 0)
                return;
            foreach (AssetProperty connectedProp in prop.GetAllConnectedProperties())
            {
                ReadAssetProperty(connectedProp, ttrgb);
            }
        }

        public bool IsCanceled()
        {
            return false;
        }

        public bool Start()
        {
            _transformationStack.Push(Transform.Identity);
            return true;
        }

        public RenderNodeAction OnViewBegin(ViewNode node)
        {
            return RenderNodeAction.Proceed;
        }

        public RenderNodeAction OnElementBegin(ElementId elementId)
        {
            Element e = _doc.GetElement(elementId);
            string uid = e.UniqueId;
            return RenderNodeAction.Proceed;
        }

        public RenderNodeAction OnInstanceBegin(InstanceNode node)
        {
            _transformationStack.Push(CurrentTransform.Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
        }

        public void OnMaterial(MaterialNode node)
        {
            currentTransparencyDouble = node.Transparency;
            currentColor = node.Color;
            currentShiniess = node.Glossiness;
            currentTransparencyint = Convert.ToInt32(node.Transparency);
            ttrgb = Util.ColorTransparencyString(currentColor, currentTransparencyint);

            ReadBitMap(node);
            GetMaterial(node);

            materialIndex++;
        }

        /// <summary>
        /// 得到材质的Asset集合
        /// </summary>
        /// <param name="node"></param>
        private void ReadBitMap(MaterialNode node)
        {
            if (node.MaterialId != ElementId.InvalidElementId)
            {
                Asset theAsset = node.GetAppearance();
                if (node.HasOverriddenAppearance)
                {
                    theAsset = node.GetAppearanceOverride();
                }
                if (theAsset == null)
                {
                    Material material = _doc.GetElement(node.MaterialId) as Material;
                    ElementId appearanceId = material.AppearanceAssetId;
                    AppearanceAssetElement appearanceElem = _doc.GetElement(appearanceId) as AppearanceAssetElement;
                    theAsset = appearanceElem.GetRenderingAsset();
                }
                if (theAsset.Size == 0)
                {
                    //Asset大小为0，则为欧特克材质
                    foreach (Asset objCurrentAsset in _objlibraryAsset)
                    {
                        if (objCurrentAsset.Name == theAsset.Name && objCurrentAsset.LibraryName == theAsset.LibraryName)
                        {
                            ReadAsset(objCurrentAsset, ttrgb);
                        }
                    }
                }
                else
                {
                    ReadAsset(theAsset, ttrgb);
                }
            }
        }

        /// <summary>
        /// 将每种材质的颜色透明度等属性添加到字典中
        /// </summary>
        /// <param name="node"></param>
        private void GetMaterial(MaterialNode node)
        {
            if (currentMterialId != node.MaterialId)
            {
                face.Add(-1);
                face.Add(Util.ColorToInt(currentColor));
                face.Add(currentTransparencyint);
                face.Add(-2);
                face.Add(-2);
                face.Add(-2);
                face.Add(-2);
                face.Add(-2);
                face.Add(-2);
                currentMterialId = node.MaterialId;

                if (!transparencys.ContainsKey(ttrgb))
                {
                    transparencys.Add(ttrgb, 1.0 - currentTransparencyDouble);
                }

                if (!colors.ContainsKey(ttrgb))
                {
                    colors.Add(ttrgb, currentColor);
                }

                if (!shiniess.ContainsKey(ttrgb))
                {
                    shiniess.Add(ttrgb, currentShiniess);
                }
            }
            else  //由于初始currentMterialId为-1，所以当第一个id为-1时，会跳过，所以这里单独添加一下。
            {
                if (materialIndex == 0)
                {
                    face.Add(-1);
                    face.Add(Util.ColorToInt(currentColor));
                    face.Add(currentTransparencyint);
                    face.Add(-2);
                    face.Add(-2);
                    face.Add(-2);
                    face.Add(-2);
                    face.Add(-2);
                    face.Add(-2);
                    currentMterialId = node.MaterialId;
                    colors.Add(ttrgb, currentColor);
                    transparencys.Add(ttrgb, currentTransparencyint);
                    shiniess.Add(ttrgb, currentShiniess);
                }
            }
        }

        public RenderNodeAction OnFaceBegin(FaceNode node)
        {
            Debug.WriteLine(" OnFaceBegin: " + node.NodeName);
            return RenderNodeAction.Proceed;
        }

        public void OnPolymesh(PolymeshTopology polymesh)
        {
            IList<XYZ> pts = polymesh.GetPoints();
            Transform t = CurrentTransform;
            pts = pts.Select(p => t.OfPoint(p)).ToList();

            var normals = polymesh.GetNormals();
            var uvs = polymesh.GetUVs();
            GetFaceIndex(polymesh, pts, normals, uvs);
        }

        /// <summary>
        /// 获取几何信息中每个三角面对应点的顶点坐标/法向坐标/UV坐标的索引
        /// 并存入face列表中
        /// </summary>
        /// <param name="polymesh"></param>
        /// <param name="pts"></param>
        /// <param name="normals"></param>
        /// <param name="uvs"></param>
        private void GetFaceIndex(PolymeshTopology polymesh, IList<XYZ> pts, IList<XYZ> normals, IList<UV> uvs)
        {
            int v1, v2, v3;
            int v4, v5, v6;
            int v7, v8, v9;
            int faceindex = 0;

            foreach (PolymeshFacet facet in polymesh.GetFacets())
            {

                v1 = _vertices.AddVertex(new PointInt(pts[facet.V1], _switch_coordinates));
                v2 = _vertices.AddVertex(new PointInt(pts[facet.V2], _switch_coordinates));
                v3 = _vertices.AddVertex(new PointInt(pts[facet.V3], _switch_coordinates));

                face.Add(v1);
                face.Add(v2);
                face.Add(v3);

                v4 = _uvs.AddVertex(new PointDouble(uvs[facet.V1]));
                v5 = _uvs.AddVertex(new PointDouble(uvs[facet.V2]));
                v6 = _uvs.AddVertex(new PointDouble(uvs[facet.V3]));
                face.Add(v4);
                face.Add(v5);
                face.Add(v6);

                if (polymesh.DistributionOfNormals == DistributionOfNormals.AtEachPoint)
                {
                    v7 = _normals.AddVertex(new PointDouble(normals[facet.V1], _switch_coordinates));
                    v8 = _normals.AddVertex(new PointDouble(normals[facet.V2], _switch_coordinates));
                    v9 = _normals.AddVertex(new PointDouble(normals[facet.V3], _switch_coordinates));
                }
                else if (polymesh.DistributionOfNormals == DistributionOfNormals.OnEachFacet)
                {
                    v7 = _normals.AddVertex(new PointDouble(normals[faceindex], _switch_coordinates));
                    v8 = v7;
                    v9 = v7;
                }
                else
                {
                    v7 = _normals.AddVertex(new PointDouble(normals[0], _switch_coordinates));
                    v8 = v7;
                    v9 = v7;
                }
                face.Add(v7);
                face.Add(v8);
                face.Add(v9);

                faceindex++;
            }
        }

        public void OnFaceEnd(FaceNode node)
        {
            Debug.WriteLine(" OnFaceEnd: " + node.NodeName);
        }

        public void OnInstanceEnd(InstanceNode node)
        {
            Debug.WriteLine(" OnInstanceEnd: " + node.NodeName);
            _transformationStack.Pop();
        }

        public void OnElementEnd(ElementId id)
        {
            Element e = _doc.GetElement(id);
            string uid = e.UniqueId;
        }

        public void OnViewEnd(ElementId elementId)
        {
            Debug.WriteLine("OnViewEnd: Id: " + elementId.IntegerValue);
        }

        /// <summary>
        /// 将Asset中读取到的位图处理并规范为1/Mats/*****.png(jpg)。
        /// 去重后存入bitmap列表中，其中包含了每个材质和每种材质下对应的所有位图信息。
        /// </summary>
        /// <param name="map"></param>
        /// <returns></returns>
        public List<string> ProcessBitMap(List<string> map)
        {
            List<string> bitmap = new List<string>();
            foreach (var m in map)
            {
                string m1 = m.Split('|')[0];
                if (m1.Substring(1, 4) == "Mats")
                {

                    m1 = m1[0] + @"/Mats/" + m1.Remove(0, 5);
                }
                if (!bitmap.Contains(m1))
                    bitmap.Add(m1);
            }
            return bitmap;
        }

        public void Finish()
        {
            WriteObj();
            List<string> unifiedbitmap = WriteMtl(ProcessBitMap(map));
            GetBitMap(unifiedbitmap);
        }

        /// <summary>
        /// 将几何信息写入obj文件
        /// </summary>
        public void WriteObj()
        {
            using (StreamWriter s = new StreamWriter(_filename))
            {
                s.WriteLine(_obj_mtllib, "model.mtl");

                foreach (PointInt key in _vertices.Keys)
                {
                    s.WriteLine(_obj_vertex, key.X / 1000, key.Y / 1000, key.Z / 1000);
                }

                foreach (PointDouble key in _normals.Keys)
                {
                    s.WriteLine(_obj_normal, key.X, key.Y, key.Z);
                }

                foreach (PointDouble key in _uvs.Keys)
                {
                    s.WriteLine(_obj_uv, key.X, key.Y);
                }

                int i = 0;
                int n = face.Count;
                while (i < n)
                {
                    int i1 = face[i++];
                    int i2 = face[i++];
                    int i3 = face[i++];

                    int i4 = face[i++];
                    int i5 = face[i++];
                    int i6 = face[i++];

                    int i7 = face[i++];
                    int i8 = face[i++];
                    int i9 = face[i++];
                    if (-1 == i1)
                    {
                        s.WriteLine(_obj_usemtl, Util.ColorTransparencyString(Util.IntToColor(i2), i3));
                    }
                    else
                    {
                        s.WriteLine(_obj_face, i1 + 1, i2 + 1, i3 + 1, i4 + 1, i5 + 1, i6 + 1, i7 + 1, i8 + 1, i9 + 1);
                    }
                }
            }
        }

        /// <summary>
        /// 将材质信息写入mtl文件。
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        public List<string> WriteMtl(List<string> bitmap)
        {
            using (StreamWriter s = new StreamWriter(Path.GetDirectoryName(_filename) + "\\model.mtl"))
            {
                DateTime currentTime = new DateTime();
                currentTime = DateTime.Now;
                s.WriteLine(_abstract, currentTime.ToString("d") + " " + currentTime.ToString("t"));
                foreach (KeyValuePair<string, Color> color in colors)
                {
                    s.Write(_mtl_newmtl_d,
                                color.Key,
                                color.Value.Red / 256.0,
                                color.Value.Green / 256.0,
                                color.Value.Blue / 256.0,
                                shiniess[color.Key],
                                transparencys[color.Key]
                                );
                    if (bitmap.Contains(color.Key))
                    {
                        for (int i = 1; i < (bitmap.Count - bitmap.IndexOf(color.Key)); i++)
                        {
                            var val = bitmap[bitmap.IndexOf(color.Key) + i];
                            if (!colors.ContainsKey(val))
                            {
                                s.WriteLine(_mtl_bitmap, val.Remove(0, 7));
                            }
                            else
                            {
                                break;
                            }
                        }
                        bitmap.Remove(color.Key);
                    }
                }
            }
            return bitmap;
        }

        /// <summary>
        /// 读取并导出所有位图。
        /// </summary>
        /// <param name="bitmap"></param>
        public void GetBitMap(List<string> bitmap)
        {
            string textureFold = Path.GetDirectoryName(_filename);
            string texturefolder = GetTextureFolder();
            foreach (var bm in bitmap)
            {
                string sourceFolder = string.Empty;
                string textureFolder = string.Empty;
                sourceFolder = Path.Combine(texturefolder, bm).Replace("/", "\\");
                int len = (bm.Split('/')[2]).Split(' ').Length;
                textureFolder = Path.Combine(textureFold, (bm.Split('/')[2]).Split(' ')[len - 1].Trim()).Replace("/", "\\");
                File.Copy(sourceFolder, textureFolder, true);
            }
        }

        /// <summary>
        /// 获取位图在系统中的公共路径。
        /// </summary>
        public static string GetTextureFolder()
        {
            if (TextureFolder == null)
            {
                var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                TextureFolder = Path.Combine(pf, @"Common Files\Autodesk Shared\Materials\Textures");
            }
            return TextureFolder;
        }
#if R2016

        public void OnDaylightPortal(DaylightPortalNode node)
        {
            {
                Debug.WriteLine("OnDaylightPortal: " + node.NodeName);
                Asset asset = node.GetAsset();
                Debug.WriteLine("OnDaylightPortal: Asset:"
                + ((asset != null) ? asset.Name : "Null"));
            }
        }

#endif
        public void OnLight(LightNode node)
        {
            Debug.WriteLine("OnLight: " + node.NodeName);
        }

        public RenderNodeAction OnLinkBegin(LinkNode node)
        {
            Debug.WriteLine(" OnLinkBegin: " + node.NodeName + " Document: " + node.GetDocument().Title + ": Id: " + node.GetSymbolId().IntegerValue);
            _transformationStack.Push(CurrentTransform.Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
        }

        public void OnLinkEnd(LinkNode node)
        {
            Debug.WriteLine(" OnLinkEnd: " + node.NodeName);
            _transformationStack.Pop();
        }

        public void OnRPC(RPCNode node)
        {
            Debug.WriteLine("OnRPC: " + node.NodeName);
        }
    }
}