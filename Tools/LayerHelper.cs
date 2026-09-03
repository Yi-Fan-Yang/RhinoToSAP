using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoToSAP.Tools
{
    public static class LayerHelper
    {
        //rhino图层锁定
        public static bool LockLayer(RhinoDoc doc, string layerName)
        {
            Layer layer = doc.Layers.FindName(layerName);
            if (layer == null)
            {
                return false;
            }
            else
            {
                layer.IsLocked = true;
                doc.Layers.Modify(layer, layer.Index, true);
                return true;
            }
        }

        //rhino图层解锁
        public static bool UnLockLayer(RhinoDoc doc, string layerName)
        {
            Layer layer = doc.Layers.FindName(layerName);
            if (layer == null)
            {
                return false;
            }
            else
            {
                layer.IsLocked = false;
                doc.Layers.Modify(layer, layer.Index, true);
                return true;
            }
        }
        //图层递归函数
        public static void GetAllChildLayers(Layer parentLayer, List<Layer> allLayers)
        {
            // 保护：父图层为空时直接返回
            if (parentLayer == null)
                return;

            allLayers.Add(parentLayer);

            // 防御性编程：GetChildren 可能返回 null
            var children = parentLayer.GetChildren();
            if (children == null)
                return;

            foreach (Layer child in children)
            {
                if (child == null) continue;
                GetAllChildLayers(child, allLayers); // 递归调用
            }
        }

        //图层过滤函数
        public static bool IsLayerInHierarchy(Layer layer, string rootLayerName)
        {
            if (string.IsNullOrEmpty(rootLayerName)) return false;
            if (layer == null) return false;
            if (layer.FullPath.StartsWith(rootLayerName+"::")|| layer.FullPath == rootLayerName) return true;
            return false;
        }
        public static bool IsObjectInLayerHierarchy(RhinoObject obj, string rootLayerName)
        {
            if (string.IsNullOrEmpty(rootLayerName)) return false;
            if (obj == null) return false;
            Layer layer = obj.Document.Layers[obj.Attributes.LayerIndex];
            return IsLayerInHierarchy(layer, rootLayerName);
        }
    }
}
