using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DebugArtTool.Runtime
{
    public class TexturePicker : MonoBehaviour
    {
        private GraphicRaycaster[] _raycasters;

        private void Update()
        {
            if (DebugArtTool.Instance == null || !DebugArtTool.Instance.IsActive) return;

            if (Input.GetMouseButtonDown(0))
            {
                if (!IsClickInsidePanel(Input.mousePosition))
                {
                    PerformPick(Input.mousePosition);
                }
            }
        }

        private bool IsClickInsidePanel(Vector2 screenPos)
        {
            Rect panelRect = DebugArtTool.Instance.PanelRect;
            float guiY = Screen.height - screenPos.y;
            return screenPos.x >= panelRect.x && screenPos.x <= panelRect.x + panelRect.width
                && guiY >= panelRect.y && guiY <= panelRect.y + panelRect.height;
        }

        private void PerformPick(Vector2 screenPos)
        {
            if (EventSystem.current == null) return;

            var pointerData = new PointerEventData(EventSystem.current) { position = screenPos };
            
            // 缓存Raycaster，如果数量有变才重新获取
            if (_raycasters == null || _raycasters.Length == 0)
                _raycasters = FindObjectsOfType<GraphicRaycaster>();

            var results = new List<RaycastResult>();

            foreach (var rc in _raycasters)
            {
                if (rc == null) continue;
                results.Clear();
                rc.Raycast(pointerData, results);

                foreach (var r in results)
                {
                    var img = r.gameObject.GetComponent<Image>();
                    if (img != null && img.sprite != null)
                    {
                        SetPickedSprite(img.sprite);
                        return;
                    }

                    var rawImg = r.gameObject.GetComponent<RawImage>();
                    if (rawImg != null && rawImg.texture != null)
                    {
                        SetPickedTexture(rawImg.texture.name, rawImg.texture as Texture2D);
                        return;
                    }
                }
            }
        }

        private void SetPickedSprite(Sprite sprite)
        {
            var tool = DebugArtTool.Instance;
            tool.InputSpriteKey = sprite.name;
            tool.PreviewTexture = sprite.texture;

            Rect spriteRect = sprite.textureRect;
            float texW = sprite.texture.width;
            float texH = sprite.texture.height;

            bool isFullTexture = Mathf.Approximately(spriteRect.width, texW) && Mathf.Approximately(spriteRect.height, texH);

            tool.PreviewIsAtlas = !isFullTexture;
            tool.PreviewTexCoords = isFullTexture ? new Rect(0, 0, 1, 1) : new Rect(spriteRect.x / texW, spriteRect.y / texH, spriteRect.width / texW, spriteRect.height / texH);

            GUIUtility.systemCopyBuffer = sprite.name;
        }

        private void SetPickedTexture(string name, Texture2D tex)
        {
            var tool = DebugArtTool.Instance;
            tool.InputSpriteKey = name;
            tool.PreviewTexture = tex;
            tool.PreviewIsAtlas = false;
            tool.PreviewTexCoords = new Rect(0, 0, 1, 1);

            GUIUtility.systemCopyBuffer = name;
        }
    }
}
