using UnityEngine;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Zone Mode hub button. The Battle City disk stays folded until this is pressed;
    /// the duel-start unfold plays when the duel scene takes over.
    /// </summary>
    public class ArDiskDuelHit : MonoBehaviour
    {
        public ArDuelDiskRig Disk;
    }

    public class ArDiskDuelButton : MonoBehaviour
    {
        public const string ButtonName = "DuelModeButton";

        public static ArDiskDuelButton Create(ArDuelDiskRig disk)
        {
            if (disk == null) return null;
            var existing = disk.transform.Find(ButtonName);
            if (existing != null)
                return existing.GetComponent<ArDiskDuelButton>();

            var go = new GameObject(ButtonName);
            go.transform.SetParent(disk.transform, false);
            var scale = ArPlaymatLayout.DiskMeshVisualScale;
            go.transform.localPosition = Vector3.zero + new Vector3(0.09f, 0.012f, 0.04f) * scale;
            go.transform.localRotation = Quaternion.Euler(78f, 0f, 0f);
            go.layer = disk.Layer;

            var button = go.AddComponent<ArDiskDuelButton>();
            var hit = go.AddComponent<ArDiskDuelHit>();
            hit.Disk = disk;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.055f, 0.012f, 0.028f) * scale;

            var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Face";
            face.transform.SetParent(go.transform, false);
            face.transform.localPosition = new Vector3(0f, 0.001f, 0f);
            face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            face.transform.localScale = new Vector3(0.050f, 0.022f, 1f) * scale;
            face.layer = disk.Layer;
            ArObjectUtil.Destroy(face.GetComponent<Collider>());
            var mr = face.GetComponent<MeshRenderer>();
            mr.sharedMaterial = ArAnimePresentation.MakeSolid(new Color(0.92f, 0.72f, 0.22f, 0.95f), "DuelButton");
            ArAnimePresentation.ConfigureHoloRenderer(mr);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            labelGo.layer = disk.Layer;
            labelGo.transform.localPosition = new Vector3(0f, 0.004f, 0f);
            labelGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = "DUEL";
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.0045f * scale;
            tm.fontSize = 48;
            tm.color = new Color(0.08f, 0.07f, 0.05f, 1f);
            tm.font = WrldzType.Body();
            return button;
        }
    }
}
