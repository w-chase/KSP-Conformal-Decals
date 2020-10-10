using System;
using System.Text;
using ConformalDecals.Util;
using UniLinq;
using UnityEngine;
using UnityEngine.Rendering;

namespace ConformalDecals {
    public class ProjectionTarget {
        // Target object data
        private readonly Transform _target;
        private readonly Part      _targetPart;
        private readonly Mesh      _targetMesh;
        private readonly Matrix4x4 _decalMatrix;
        private readonly Vector3   _decalNormal;
        private readonly Vector3   _decalTangent;
        private readonly bool      _useBaseNormal;

        // property block
        private readonly MaterialPropertyBlock _decalMPB;

        public ProjectionTarget(Part targetPart, Transform target, MeshRenderer renderer, MeshFilter filter,
            Matrix4x4 orthoMatrix, Transform projector, bool useBaseNormal) {

            _targetPart = targetPart;
            _target = target;
            _targetMesh = filter.sharedMesh;
            _useBaseNormal = useBaseNormal;
            _decalMPB = new MaterialPropertyBlock();

            var projectorToTargetMatrix = target.worldToLocalMatrix * projector.localToWorldMatrix;

            _decalMatrix = orthoMatrix * projectorToTargetMatrix.inverse;
            _decalNormal = projectorToTargetMatrix.MultiplyVector(Vector3.back).normalized;
            _decalTangent = projectorToTargetMatrix.MultiplyVector(Vector3.right).normalized;

            SetupMPB(renderer.sharedMaterial);
        }

        public ProjectionTarget(ConfigNode node, Vessel vessel, bool useBaseNormal) {
            var flightID = (uint) ParseUtil.ParseInt(node, "part");
            var targetPath = ParseUtil.ParseString(node, "targetPath");
            var targetName = ParseUtil.ParseString(node, "targetName");
            
            _decalMatrix = ParseUtil.ParseMatrix4x4(node, "decalMatrix");
            _decalNormal = ParseUtil.ParseVector3(node, "decalNormal");
            _decalTangent = ParseUtil.ParseVector3(node, "decalTangent");
            _useBaseNormal = useBaseNormal;
            _decalMPB = new MaterialPropertyBlock();

            _targetPart = vessel[flightID];
            if (_targetPart == null) throw new IndexOutOfRangeException("Vessel returned null part");
            _target = LoadTransformPath(targetPath, _targetPart.transform);
            if (_target.name != targetName) throw new FormatException("Target name does not match");

            var renderer = _target.GetComponent<MeshRenderer>();
            var filter = _target.GetComponent<MeshFilter>();

            if (!ValidateTarget(_target, renderer, filter)) throw new FormatException("Invalid target");

            _targetMesh = filter.sharedMesh;

            SetupMPB(renderer.sharedMaterial);
        }

        private void SetupMPB(Material targetMaterial) {
            _decalMPB.SetMatrix(DecalPropertyIDs._ProjectionMatrix, _decalMatrix);
            _decalMPB.SetVector(DecalPropertyIDs._DecalNormal, _decalNormal);
            _decalMPB.SetVector(DecalPropertyIDs._DecalTangent, _decalTangent);

            if (_useBaseNormal && targetMaterial.HasProperty(DecalPropertyIDs._BumpMap)) {
                _decalMPB.SetTexture(DecalPropertyIDs._BumpMap, targetMaterial.GetTexture(DecalPropertyIDs._BumpMap));

                var normalScale = targetMaterial.GetTextureScale(DecalPropertyIDs._BumpMap);
                var normalOffset = targetMaterial.GetTextureOffset(DecalPropertyIDs._BumpMap);

                _decalMPB.SetVector(DecalPropertyIDs._BumpMap_ST, new Vector4(normalScale.x, normalScale.y, normalOffset.x, normalOffset.y));
            }
            else {
                _decalMPB.SetTexture(DecalPropertyIDs._BumpMap, DecalConfig.BlankNormal);
            }
        }

        public void Render(Material decalMaterial, MaterialPropertyBlock partMPB, Camera camera) {
            _decalMPB.SetFloat(PropertyIDs._RimFalloff, partMPB.GetFloat(PropertyIDs._RimFalloff));
            _decalMPB.SetColor(PropertyIDs._RimColor, partMPB.GetColor(PropertyIDs._RimColor));

            Graphics.DrawMesh(_targetMesh, _target.localToWorldMatrix, decalMaterial, 0, camera, 0, _decalMPB, ShadowCastingMode.Off, true);
        }

        public ConfigNode Save() {
            var node = new ConfigNode("TARGET");
            node.AddValue("part", _targetPart.flightID);
            node.AddValue("decalMatrix", _decalMatrix);
            node.AddValue("decalNormal", _decalNormal);
            node.AddValue("decalTangent", _decalTangent);
            node.AddValue("targetPath", SaveTransformPath(_target, _targetPart.transform)); // used to find the target transform
            node.AddValue("targetName", _target.name); // used to validate the mesh has not changed since last load

            return node;
        }


        public static bool ValidateTarget(Transform target, MeshRenderer renderer, MeshFilter filter) {
            if (renderer == null) return false;
            if (filter == null) return false;
            if (!target.gameObject.activeInHierarchy) return false;

            var material = renderer.material;
            if (material == null) return false;
            if (DecalConfig.IsBlacklisted(material.shader)) return false;

            if (filter.sharedMesh == null) return false;

            return true;
        }

        private static string SaveTransformPath(Transform leaf, Transform root) {
            var builder = new StringBuilder(leaf.name);
            var current = leaf.parent;

            while (current != root) {
                builder.Insert(0, "/");
                builder.Insert(0, current.GetSiblingIndex());
                current = current.parent;
                if (current == null) throw new FormatException("Leaf does not exist as a child of root");
            }

            return builder.ToString();
        }

        private static Transform LoadTransformPath(string path, Transform root) {
            var indices = path.Split('/').Select(int.Parse);
            var current = root;

            foreach (var index in indices) {
                if (index > current.childCount) throw new FormatException("Child index path is invalid");
                current = current.GetChild(index);
            }

            return current;
        }
    }
}