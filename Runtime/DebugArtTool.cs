// ============================================================================
// DebugArtTool.cs
// 运行时美术图片替换调试工具 —— 核心管理器
//
// F2 打开/关闭。面板显示：图片名称 + 原图预览 + 上传按钮。
// 点击游戏中图片自动吸取名称和预览，点上传按钮选图替换。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace DebugArtTool.Runtime
{
    public class DebugArtTool : MonoBehaviour
    {
        // =====================================================================
        // 单例
        // =====================================================================
        private static DebugArtTool _instance;
        public static DebugArtTool Instance => _instance;

        // =====================================================================
        // 替换字典：key = 原始名称, value = 替换用 Texture2D
        // =====================================================================
        private readonly Dictionary<string, Texture2D> _replacementTexDict = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
        private readonly List<Texture2D> _tempTextures = new List<Texture2D>();
        private readonly List<string> _blobUrls = new List<string>();

        // =====================================================================
        // 面板状态
        // =====================================================================
        private bool _showPanel = false;
        public bool IsActive => _showPanel;

        // 面板窗口 Rect（支持拖拽移动）
        private Rect _windowRect = new Rect(Screen.width - 376f, 16f, 360f, 100f);
        private const int WINDOW_ID = 91827; // 唯一窗口 ID，避免和其他 GUI.Window 冲突

        /// <summary>
        /// 当前面板矩形（TexturePicker 用于判断点击是否在面板内）
        /// </summary>
        public Rect PanelRect => _windowRect;

        // 当前选中的图片信息（由 TexturePicker 写入）
        [NonSerialized] public string InputSpriteKey = "";
        [NonSerialized] public Texture2D PreviewTexture = null;   // 原图预览纹理
        [NonSerialized] public Rect PreviewTexCoords = new Rect(0, 0, 1, 1); // Atlas 中的 UV 区域
        [NonSerialized] public bool PreviewIsAtlas = false;       // 是否需要用 TexCoords 裁剪

        private string _statusMessage = "";

        // GUI 样式
        private bool _styleInited = false;
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _nameLabelStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _statusStyle;

        // =====================================================================
        // jslib
        // =====================================================================
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void WebGL_OpenFilePicker(string gameObjectName, string spriteKey);
        [DllImport("__Internal")]
        private static extern void WebGL_RevokeBlobUrl(string blobUrl);
#else
        private static void WebGL_OpenFilePicker(string gameObjectName, string spriteKey)
        {
            Debug.Log($"[DebugArtTool] WebGL_OpenFilePicker 仅在 WebGL 平台生效. key={spriteKey}");
        }
        private static void WebGL_RevokeBlobUrl(string blobUrl)
        {
            Debug.Log($"[DebugArtTool] WebGL_RevokeBlobUrl 仅在 WebGL 平台生效. url={blobUrl}");
        }
#endif

        // =====================================================================
        // 生命周期
        // =====================================================================
        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            CleanupAllTemporaryAssets();
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
            {
                _showPanel = !_showPanel;
            }
        }

        // =====================================================================
        // GUI 样式初始化
        // =====================================================================
        private void InitStyles()
        {
            if (_styleInited) return;
            _styleInited = true;

            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(14, 14, 28, 10)
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                wordWrap = true
            };

            _nameLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 20,
                fixedHeight = 48f
            };

            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                wordWrap = true,
                fontStyle = FontStyle.Italic
            };
            _statusStyle.normal.textColor = Color.yellow;
        }

        // =====================================================================
        // OnGUI — 使用 GUI.Window 实现可拖拽面板
        // =====================================================================
        private void OnGUI()
        {
            if (!_showPanel) return;
            InitStyles();

            // 动态计算面板高度
            float ph = CalcPanelHeight();
            _windowRect.width = 360f;
            _windowRect.height = ph;

            // 限制面板不超出屏幕
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);

            // GUI.Window 自带标题栏拖拽
            _windowRect = GUI.Window(WINDOW_ID, _windowRect, DrawPanelContent, "美术调试 (F2)", _windowStyle);
        }

        /// <summary>
        /// 面板内容绘制（由 GUI.Window 回调）
        /// </summary>
        private void DrawPanelContent(int windowId)
        {
            float contentW = _windowRect.width - 28f; // 减去左右 padding

            GUILayout.Space(8f);

            if (string.IsNullOrEmpty(InputSpriteKey))
            {
                GUILayout.Label("点击游戏中的图片\n自动吸取名称并预览", _nameLabelStyle);
            }
            else
            {
                // 图片名称
                GUILayout.Label(InputSpriteKey, _nameLabelStyle);
                GUILayout.Space(8f);

                // 预览图
                if (PreviewTexture != null)
                {
                    float previewW, previewH;
                    CalcPreviewSize(contentW, 300f, out previewW, out previewH);

                    float offsetX = (contentW - previewW) * 0.5f;
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(offsetX);

                    Rect previewRect = GUILayoutUtility.GetRect(previewW, previewH,
                        GUILayout.Width(previewW), GUILayout.Height(previewH));

                    if (PreviewIsAtlas)
                    {
                        GUI.DrawTextureWithTexCoords(previewRect, PreviewTexture, PreviewTexCoords);
                    }
                    else
                    {
                        GUI.DrawTexture(previewRect, PreviewTexture, ScaleMode.ScaleToFit);
                    }

                    GUILayout.EndHorizontal();
                    GUILayout.Space(10f);
                }

                // 上传按钮
                if (GUILayout.Button("上传替换图片", _buttonStyle))
                {
                    _statusMessage = "等待选择图片...";
                    WebGL_OpenFilePicker(gameObject.name, InputSpriteKey);
                }
            }

            // 状态信息
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUILayout.Space(4f);
                GUILayout.Label(_statusMessage, _statusStyle);
            }

            // 让整个标题栏区域可拖拽（标题栏高度约 20px）
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 24f));
        }

        /// <summary>
        /// 预计算面板高度
        /// </summary>
        private float CalcPanelHeight()
        {
            float ph = 28f; // window title
            ph += 8f;       // top space

            if (string.IsNullOrEmpty(InputSpriteKey))
            {
                ph += 60f;
            }
            else
            {
                ph += 32f; // 名称
                ph += 8f;
                if (PreviewTexture != null)
                {
                    float contentW = 360f - 28f;
                    CalcPreviewSize(contentW, 300f, out _, out float previewH);
                    ph += previewH + 10f;
                }
                ph += 56f; // 按钮
            }

            if (!string.IsNullOrEmpty(_statusMessage)) ph += 30f;
            ph += 16f; // bottom padding
            return ph;
        }

        /// <summary>
        /// 计算预览图在面板中的显示尺寸（保持宽高比，不超过 maxW × maxH）
        /// </summary>
        private void CalcPreviewSize(float maxW, float maxH, out float w, out float h)
        {
            float srcW, srcH;
            if (PreviewIsAtlas)
            {
                srcW = PreviewTexture.width * PreviewTexCoords.width;
                srcH = PreviewTexture.height * PreviewTexCoords.height;
            }
            else
            {
                srcW = PreviewTexture.width;
                srcH = PreviewTexture.height;
            }

            if (srcW <= 0 || srcH <= 0) { w = maxW; h = maxH; return; }

            float scale = Mathf.Min(maxW / srcW, maxH / srcH, 1f); // 不放大
            w = srcW * scale;
            h = srcH * scale;

            // 至少 60px 可见
            if (w < 60f) { w = 60f; h = w * (srcH / srcW); }
        }

        // =====================================================================
        // JS 回调
        // =====================================================================
        public void OnImageSelected(string message)
        {
            int sep = message.IndexOf('|');
            if (sep < 0) { Debug.LogError("[DebugArtTool] 无效消息: " + message); return; }

            string spriteKey = message.Substring(0, sep);
            string blobUrl = message.Substring(sep + 1);
            _statusMessage = $"下载中...";
            StartCoroutine(DownloadAndApply(spriteKey, blobUrl));
        }

        // =====================================================================
        // 下载 & 替换
        // =====================================================================
        private IEnumerator DownloadAndApply(string spriteKey, string blobUrl)
        {
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(blobUrl, false))
            {
                yield return request.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                if (request.result != UnityWebRequest.Result.Success)
#else
                if (request.isNetworkError || request.isHttpError)
#endif
                {
                    _statusMessage = $"下载失败: {request.error}";
                    yield break;
                }

                Texture2D downloadedTex = DownloadHandlerTexture.GetContent(request);
                downloadedTex.filterMode = FilterMode.Bilinear;
                downloadedTex.anisoLevel = 4;
                downloadedTex.wrapMode = TextureWrapMode.Clamp;
                downloadedTex.name = spriteKey;

                if (_replacementTexDict.TryGetValue(spriteKey, out Texture2D oldTex))
                {
                    ClearSpriteCacheForKey(spriteKey);
                    _tempTextures.Remove(oldTex);
                    Destroy(oldTex);
                }

                _replacementTexDict[spriteKey] = downloadedTex;
                _tempTextures.Add(downloadedTex);

                _blobUrls.Add(blobUrl);
                WebGL_RevokeBlobUrl(blobUrl);

                int count = ScanAndReplaceAll();
                _statusMessage = $"已替换 {count} 处";
            }
        }

        // =====================================================================
        // 暴力扫描替换（保留 border / imageType）
        // =====================================================================
        public int ScanAndReplaceAll()
        {
            if (_replacementTexDict.Count == 0) return 0;
            int n = 0;

            Image[] allImages = Resources.FindObjectsOfTypeAll<Image>();
            foreach (Image img in allImages)
            {
                if (img == null || img.sprite == null || IsEditorObject(img.hideFlags)) continue;
                if (_replacementTexDict.TryGetValue(img.sprite.name, out Texture2D newTex))
                {
                    if (img.sprite.texture == newTex) continue;
                    img.sprite = GetOrCreateSprite(img.sprite.name, newTex, img.sprite);
                    n++;
                }
            }

            RawImage[] allRawImages = Resources.FindObjectsOfTypeAll<RawImage>();
            foreach (RawImage ri in allRawImages)
            {
                if (ri == null || ri.texture == null || IsEditorObject(ri.hideFlags)) continue;
                if (_replacementTexDict.TryGetValue(ri.texture.name, out Texture2D newTex))
                {
                    if (ri.texture == newTex) continue;
                    ri.texture = newTex;
                    n++;
                }
            }

            SpriteRenderer[] allSR = Resources.FindObjectsOfTypeAll<SpriteRenderer>();
            foreach (SpriteRenderer sr in allSR)
            {
                if (sr == null || sr.sprite == null || IsEditorObject(sr.hideFlags)) continue;
                if (_replacementTexDict.TryGetValue(sr.sprite.name, out Texture2D newTex))
                {
                    if (sr.sprite.texture == newTex) continue;
                    sr.sprite = GetOrCreateSprite(sr.sprite.name, newTex, sr.sprite);
                    n++;
                }
            }

            MeshRenderer[] allMR = Resources.FindObjectsOfTypeAll<MeshRenderer>();
            foreach (MeshRenderer mr in allMR)
            {
                if (mr == null || IsEditorObject(mr.hideFlags)) continue;
                Material mat = mr.sharedMaterial;
                if (mat != null && mat.mainTexture != null &&
                    _replacementTexDict.TryGetValue(mat.mainTexture.name, out Texture2D newTex))
                {
                    if (mat.mainTexture == newTex) continue;
                    mr.material.mainTexture = newTex;
                    n++;
                }
            }

            return n;
        }

        // =====================================================================
        // Sprite 创建（保留 border）
        // =====================================================================
        private Sprite GetOrCreateSprite(string spriteKey, Texture2D newTex, Sprite original)
        {
            Vector4 border = original.border;
            Rect oRect = original.rect;
            Vector2 normPivot = new Vector2(
                oRect.width > 0 ? original.pivot.x / oRect.width : 0.5f,
                oRect.height > 0 ? original.pivot.y / oRect.height : 0.5f
            );
            float ppu = original.pixelsPerUnit;

            string cacheKey = $"{spriteKey}|{border.x},{border.y},{border.z},{border.w}";
            if (_spriteCache.TryGetValue(cacheKey, out Sprite cached) && cached != null && cached.texture == newTex)
                return cached;

            if (cached != null) Destroy(cached);

            Sprite sp = Sprite.Create(newTex,
                new Rect(0, 0, newTex.width, newTex.height),
                normPivot, ppu, 0, SpriteMeshType.FullRect, border);
            sp.name = spriteKey;
            _spriteCache[cacheKey] = sp;
            return sp;
        }

        private void ClearSpriteCacheForKey(string spriteKey)
        {
            string prefix = spriteKey + "|";
            var toRemove = new List<string>();
            foreach (var kvp in _spriteCache)
            {
                if (kvp.Key.StartsWith(prefix))
                {
                    if (kvp.Value != null) Destroy(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (string k in toRemove) _spriteCache.Remove(k);
        }

        private static bool IsEditorObject(HideFlags f)
        {
            return f == HideFlags.NotEditable || f == HideFlags.HideAndDontSave;
        }

        // =====================================================================
        // 拦截器接口
        // =====================================================================
        public bool TryGetReplacement(string spriteKey, Sprite originalSprite, out Sprite replacement)
        {
            if (_replacementTexDict.TryGetValue(spriteKey, out Texture2D newTex))
            {
                replacement = GetOrCreateSprite(spriteKey, newTex, originalSprite);
                return true;
            }
            replacement = null;
            return false;
        }

        public bool TryGetReplacementTexture(string spriteKey, out Texture2D replacement)
        {
            return _replacementTexDict.TryGetValue(spriteKey, out replacement);
        }

        public void RegisterReplacement(string spriteKey, Texture2D texture)
        {
            if (_replacementTexDict.TryGetValue(spriteKey, out Texture2D oldTex))
            {
                ClearSpriteCacheForKey(spriteKey);
                _tempTextures.Remove(oldTex);
                Destroy(oldTex);
            }
            _replacementTexDict[spriteKey] = texture;
        }

        public void RemoveReplacement(string spriteKey)
        {
            ClearSpriteCacheForKey(spriteKey);
            if (_replacementTexDict.TryGetValue(spriteKey, out Texture2D oldTex))
            {
                _tempTextures.Remove(oldTex);
                Destroy(oldTex);
                _replacementTexDict.Remove(spriteKey);
            }
        }

        // =====================================================================
        // 内存管理
        // =====================================================================
        private void CleanupAllTemporaryAssets()
        {
            foreach (var kvp in _spriteCache)
                if (kvp.Value != null) Destroy(kvp.Value);
            _spriteCache.Clear();
            _replacementTexDict.Clear();
            foreach (var tex in _tempTextures)
                if (tex != null) Destroy(tex);
            _tempTextures.Clear();
            foreach (string url in _blobUrls) WebGL_RevokeBlobUrl(url);
            _blobUrls.Clear();
        }
    }
}
