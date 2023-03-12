// Licensed under the MIT License. See LICENSE in the project root for license information.

using ElevenLabs.History;
using ElevenLabs.User;
using ElevenLabs.Voices;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ElevenLabs.Editor
{
    public sealed class ElevenLabsEditorWindow : EditorWindow
    {
        #region UX Content

        private const int InnerLabelIndentLevel = 13;
        private const int TabWidth = 18;

        private const float InnerLabelWidth = 1.9f;
        private const float DefaultColumnWidth = 96f;
        private const float WideColumnWidth = 128f;
        private const float SettingsLabelWidth = 1.56f;

        private static readonly GUIContent saveDirectoryContent = new GUIContent("Save Directory");

        private static readonly GUIContent guiTitleContent = new GUIContent("Eleven Labs Dashboard");

        private static readonly GUIContent voiceModelContent = new GUIContent("Voice Model");

        private static readonly GUIContent stabilityContent = new GUIContent("Stability");

        private static readonly GUIContent moreVariableContent = new GUIContent("More Variable", "Increasing variability can make speech more expressive with output varying between re-generations. It can also lead to instabilities.");

        private static readonly GUIContent moreStableContent = new GUIContent("More Stable", "Increasing stability will make the voice more consistent between re-generations, but it can also make it sounds a bit monotone. On longer text fragments we recommend lowering this value.");

        private static readonly GUIContent clarityContent = new GUIContent("Clarity + Similarity Enhancement");

        private static readonly GUIContent lowClarityContent = new GUIContent("Low", "Low values are recommended if background artifacts are present in generated speech.");

        private static readonly GUIContent highClarityContent = new GUIContent("High", "Recommended. High enhancement boosts overall voice clarity and target speaker similarity. Very high values can cause artifacts, so adjusting this setting to find the optimal value is encouraged.");

        private static readonly GUIContent addNewSampleContent = new GUIContent("Add new Sample(s)");

        private static readonly string[] tabTitles = { "Speech Synthesis", "Voice Lab", "History" };

        private static GUIStyle boldCenteredHeaderStyle;

        private static GUIStyle BoldCenteredHeaderStyle
        {
            get
            {
                if (boldCenteredHeaderStyle == null)
                {
                    var editorStyle = EditorGUIUtility.isProSkin ? EditorStyles.whiteLargeLabel : EditorStyles.largeLabel;

                    if (editorStyle != null)
                    {
                        boldCenteredHeaderStyle = new GUIStyle(editorStyle)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            fontSize = 18,
                            padding = new RectOffset(0, 0, -8, -8)
                        };
                    }
                }

                return boldCenteredHeaderStyle;
            }
        }

        private static GUIStyle rightMiddleAlignedLabel;

        private static GUIStyle RightMiddleAlignedLabel => rightMiddleAlignedLabel ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleRight,
        };

        private static string DefaultSaveDirectoryKey => $"{Application.productName}_ElevenLabs_EditorDownloadDirectory";

        private static string DefaultSaveDirectory => $"{Application.streamingAssetsPath}/{nameof(ElevenLabs)}";

        #endregion UX Content

        private static ElevenLabsClient api;

        private static string editorDownloadDirectory = string.Empty;

        private static bool hasFetchedUserInfo;

        private static bool isFetchingUserInfo;

        private static SubscriptionInfo userInfo;

        private static bool hasFetchedVoices;

        private static bool isFetchingVoices;

        private static IReadOnlyList<Voice> voices = new List<Voice>();

        private static GUIContent[] voiceOptions = Array.Empty<GUIContent>();

        private static Voice currentVoiceOption;

        private static VoiceSettings currentVoiceSettings;

        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> voiceLabels = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();

        private static bool hasFetchedHistory;

        private static bool isFetchingHistory;

        private static IReadOnlyList<HistoryItem> history = new List<HistoryItem>();

        private static readonly ConcurrentBag<string> downloadedClips = new ConcurrentBag<string>();

        [SerializeField]
        private int tab;

        [SerializeField]
        private string currentVoiceId;

        [SerializeField]
        private Vector2 voiceSettingsSliderValues = Vector2.zero;

        private Vector2 scrollPosition = Vector2.zero;

        private string speechSynthesisTextInput = string.Empty;

        [SerializeField]
        private List<AudioClip> newSampleClips;

        private string tempLabelKey;

        private string tempLabelValue;

        [MenuItem("ElevenLabs/Dashboard")]
        private static void OpenWindow()
        {
            // Dock it next to the Scene View.
            var instance = GetWindow<ElevenLabsEditorWindow>(typeof(SceneView));
            instance.Show();
            instance.titleContent = guiTitleContent;
        }

        private void OnEnable()
        {
            titleContent = guiTitleContent;
            minSize = new Vector2(WideColumnWidth * 5, WideColumnWidth * 4);
        }

        private void OnFocus()
        {
            api ??= new ElevenLabsClient();

            if (!hasFetchedUserInfo ||
                userInfo == null)
            {
                hasFetchedUserInfo = true;
                FetchUserInfo();
            }

            if (!hasFetchedVoices)
            {
                hasFetchedVoices = true;
                FetchVoices();
            }

            if (!hasFetchedHistory)
            {
                hasFetchedHistory = true;
                FetchHistory();
            }
            else
            {
                CheckHistory();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandWidth(true));
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(TabWidth);
            EditorGUILayout.BeginVertical();
            { // Begin Header
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Eleven Labs Dashboard", BoldCenteredHeaderStyle);
                EditorGUILayout.Space();

                if (api == null ||
                    string.IsNullOrWhiteSpace(api.ElevenLabsAuthentication.ApiKey))
                {
                    EditorGUILayout.HelpBox(
                        $"No valid {nameof(ElevenLabsConfigurationSettings)} was found. This tool requires that you set your API key.",
                        MessageType.Error);

                    return;
                }

                EditorGUILayout.Space();
                tab = GUILayout.Toolbar(tab, tabTitles, GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField(saveDirectoryContent);

                if (string.IsNullOrWhiteSpace(editorDownloadDirectory))
                {
                    editorDownloadDirectory = EditorPrefs.GetString(DefaultSaveDirectoryKey, DefaultSaveDirectory);
                }

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.TextField(editorDownloadDirectory, GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("Reset", GUILayout.Width(WideColumnWidth)))
                    {
                        editorDownloadDirectory = DefaultSaveDirectory;
                        EditorPrefs.SetString(DefaultSaveDirectoryKey, editorDownloadDirectory);
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("Change Save Directory", GUILayout.ExpandWidth(true)))
                    {
                        EditorApplication.delayCall += () =>
                        {
                            var result = EditorUtility.OpenFolderPanel("Save Directory", editorDownloadDirectory, string.Empty);

                            if (!string.IsNullOrWhiteSpace(result))
                            {
                                editorDownloadDirectory = result;
                                EditorPrefs.SetString(DefaultSaveDirectoryKey, editorDownloadDirectory);
                            }
                        };
                    }
                }
                EditorGUILayout.EndHorizontal();
            } // End Header
            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            EditorGUI.indentLevel++;

            switch (tab)
            {
                case 0:
                    RenderSpeechSynthesis();
                    break;
                case 1:
                    RenderVoiceLab();
                    break;
                case 2:
                    RenderHistory();
                    break;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private static async void FetchUserInfo()
        {
            if (isFetchingUserInfo) { return; }
            isFetchingUserInfo = true;

            try
            {
                userInfo = await api.UserEndpoint.GetSubscriptionInfoAsync();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                isFetchingUserInfo = false;
            }
        }

        private static async void FetchVoices()
        {
            if (isFetchingVoices) { return; }
            isFetchingVoices = true;

            try
            {
                voices = await api.VoicesEndpoint.GetAllVoicesAsync();
                voiceOptions = voices.OrderBy(voice => voice.Name).Select(voice => new GUIContent($"{voice.Category}/{voice.Name}")).ToArray();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                isFetchingVoices = false;
                voiceLabels.Clear();
            }
        }

        private static async void FetchHistory()
        {
            if (isFetchingHistory) { return; }
            isFetchingHistory = true;

            try
            {
                history = await api.HistoryEndpoint.GetHistoryAsync().ConfigureAwait(true);
                CheckHistory();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                isFetchingHistory = false;
            }
        }

        private static void CheckHistory()
        {
            if (history == null) { return; }
            var assets = AssetDatabase.FindAssets($"t:{nameof(AudioClip)}");
            downloadedClips.Clear();

            foreach (var guid in assets)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var assetName = Path.GetFileNameWithoutExtension(assetPath);

                if (history.Any(item => item.Id == assetName) ||
                    history.Any(item => item.TextHash == assetName))
                {
                    downloadedClips.Add(assetPath);
                }
            }
        }

        #region Voice Synthesis

        private void RenderSpeechSynthesis()
        {
            EditorGUILayout.BeginHorizontal();
            { // Header
                EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                GUI.enabled = !isFetchingVoices || !isFetchingUserInfo;

                if (GUILayout.Button("Refresh", GUILayout.Width(DefaultColumnWidth)))
                {
                    EditorApplication.delayCall += () =>
                    {
                        FetchVoices();
                        FetchUserInfo();
                    };
                }

                EditorGUILayout.Space(10);
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            { // Body
                var prevLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = WideColumnWidth * SettingsLabelWidth;

                EditorGUILayout.BeginHorizontal();
                { // voice dropdown
                    var voiceIndex = -1;

                    currentVoiceOption ??= !string.IsNullOrWhiteSpace(currentVoiceId)
                        ? voices?.FirstOrDefault(voice => voice.Id == currentVoiceId)
                        : voices?.FirstOrDefault();

                    if (currentVoiceOption != null)
                    {
                        for (var i = 0; i < voiceOptions.Length; i++)
                        {
                            if (voiceOptions[i].text.Contains(currentVoiceOption.Name))
                            {
                                voiceIndex = i;
                                break;
                            }
                        }

                        if (currentVoiceSettings == null ||
                            voiceSettingsSliderValues == Vector2.zero)
                        {
                            GetDefaultVoiceSettings(currentVoiceOption);
                        }
                    }

                    EditorGUI.BeginChangeCheck();
                    voiceIndex = EditorGUILayout.Popup(voiceModelContent, voiceIndex, voiceOptions);

                    if (EditorGUI.EndChangeCheck())
                    {
                        currentVoiceOption = voices?.FirstOrDefault(voice => voiceOptions[voiceIndex].text.Contains($"{voice.Category}/{voice.Name}"));
                        currentVoiceId = currentVoiceOption!.Id;
                        EditorApplication.delayCall += () => GetDefaultVoiceSettings(currentVoiceOption);
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();

                GUI.enabled = currentVoiceSettings != null && !isGettingDefaultVoiceSettings;
                EditorGUI.BeginChangeCheck();

                voiceSettingsSliderValues.x = EditorGUILayout.Slider(stabilityContent, voiceSettingsSliderValues.x, 0f, 1f);
                EditorGUILayout.BeginHorizontal();
                {
                    StartIndent(InnerLabelIndentLevel);
                    EditorGUIUtility.labelWidth = WideColumnWidth * InnerLabelWidth;
                    EditorGUILayout.LabelField(moreVariableContent, GUILayout.ExpandWidth(true));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(moreStableContent, RightMiddleAlignedLabel, GUILayout.ExpandWidth(true));
                    EditorGUIUtility.labelWidth = WideColumnWidth * SettingsLabelWidth;
                    EndIndent(InnerLabelIndentLevel);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(10);
                voiceSettingsSliderValues.y = EditorGUILayout.Slider(clarityContent, voiceSettingsSliderValues.y, 0f, 1f);
                EditorGUILayout.BeginHorizontal();
                {
                    StartIndent(InnerLabelIndentLevel);
                    EditorGUIUtility.labelWidth = WideColumnWidth * InnerLabelWidth;
                    EditorGUILayout.LabelField(lowClarityContent, GUILayout.ExpandWidth(true));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(highClarityContent, RightMiddleAlignedLabel, GUILayout.ExpandWidth(true));
                    EditorGUIUtility.labelWidth = WideColumnWidth * SettingsLabelWidth;
                    EndIndent(InnerLabelIndentLevel);
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(10);
                EditorGUILayout.BeginVertical();
                EditorGUILayout.BeginHorizontal();
                { // Text Area Header
                    EditorGUILayout.LabelField("Text", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Reset to Default", GUILayout.Width(WideColumnWidth)))
                    {
                        EditorApplication.delayCall += () => GetDefaultVoiceSettings(currentVoiceOption);
                    }
                    else
                    {
                        if (EditorGUI.EndChangeCheck())
                        {
                            currentVoiceSettings = new VoiceSettings(voiceSettingsSliderValues.x, voiceSettingsSliderValues.y);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();

                GUI.enabled = true;
                EditorGUIUtility.labelWidth = prevLabelWidth;
                GUILayout.Space(10);

                EditorGUI.BeginChangeCheck();
                EditorStyles.textField.wordWrap = true;
                speechSynthesisTextInput = EditorGUILayout.TextArea(speechSynthesisTextInput, GUILayout.ExpandHeight(true));

                if (EditorGUI.EndChangeCheck())
                {
                    if (speechSynthesisTextInput.Length > 5000)
                    {
                        speechSynthesisTextInput = speechSynthesisTextInput[..5000];
                    }
                }

                GUILayout.Space(10);
                EditorGUILayout.BeginHorizontal();
                { // Text area footer
                    EditorGUILayout.LabelField(new GUIContent($"{speechSynthesisTextInput.Length} / 5000"), GUILayout.ExpandWidth(true));
                    GUILayout.FlexibleSpace();
                    var remainingCharacters = 0;

                    if (userInfo != null)
                    {
                        remainingCharacters = userInfo.CharacterLimit - userInfo.CharacterCount;
                    }

                    EditorGUILayout.LabelField(new GUIContent($"Total quota remaining: {remainingCharacters}"), RightMiddleAlignedLabel, GUILayout.ExpandWidth(true));
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(10);

                EditorGUILayout.BeginHorizontal();
                {
                    GUI.enabled = !isSynthesisRunning;
                    GUILayout.Space(TabWidth);

                    if (GUILayout.Button("Generate", GUILayout.ExpandWidth(true)))
                    {
                        EditorApplication.delayCall += GenerateSynthesizedText;
                    }
                    GUI.enabled = true;
                }
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.Space(10);
            GUILayout.EndVertical();
            GUILayout.Space(10);
            GUILayout.EndHorizontal();
        }

        private static bool isSynthesisRunning;

        private async void GenerateSynthesizedText()
        {
            if (isSynthesisRunning) { return; }
            isSynthesisRunning = true;

            try
            {
                if (string.IsNullOrWhiteSpace(speechSynthesisTextInput))
                {
                    throw new ArgumentNullException(nameof(speechSynthesisTextInput));
                }

                if (currentVoiceOption == null)
                {
                    throw new ArgumentNullException(nameof(currentVoiceOption));
                }

                if (currentVoiceSettings == null)
                {
                    throw new ArgumentNullException(nameof(currentVoiceSettings));
                }

                var (clipPath, audioClip) = await api.TextToSpeechEndpoint.TextToSpeechAsync(speechSynthesisTextInput, currentVoiceOption, currentVoiceSettings, editorDownloadDirectory);

                if (clipPath.Contains(Application.dataPath))
                {
                    var importPath = clipPath.Replace(Application.dataPath, "Assets").Replace("\\", "/");
                    AssetDatabase.ImportAsset(importPath);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    EditorGUIUtility.PingObject(audioClip);
                    Selection.activeObject = audioClip;
                }

                FetchUserInfo();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                isSynthesisRunning = false;
                speechSynthesisTextInput = string.Empty;
            }
        }

        private static bool isGettingDefaultVoiceSettings;
        private string lastTextControl;

        private async void GetDefaultVoiceSettings(Voice voice)
        {
            if (isGettingDefaultVoiceSettings) { return; }
            isGettingDefaultVoiceSettings = true;

            try
            {
                currentVoiceSettings = await api.VoicesEndpoint.GetVoiceSettingsAsync(voice);
                voiceSettingsSliderValues = new Vector2(currentVoiceSettings.Stability, currentVoiceSettings.SimilarityBoost);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                isGettingDefaultVoiceSettings = false;
            }
        }

        #endregion Voice Synthesis

        #region Voice Lab

        private void RenderVoiceLab()
        {
            string voiceCount;

            if (!isFetchingVoices && userInfo != null)
            {
                var generatedCount = voices.Count(voice => voice.Category is "generated" or "cloned");
                voiceCount = $"{generatedCount}/{userInfo.VoiceLimit}";
            }
            else
            {
                voiceCount = "~/~";
            }

            EditorGUILayout.BeginHorizontal();
            { // Header
                EditorGUILayout.LabelField($"Voices {voiceCount}", EditorStyles.boldLabel, GUILayout.Width(DefaultColumnWidth));

                GUI.enabled = !isFetchingVoices;

                if (GUILayout.Button("Create New Voice", GUILayout.Width(WideColumnWidth)))
                {
                    EditorApplication.delayCall += ShowCreateVoiceEditorPopup;
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Refresh", GUILayout.Width(DefaultColumnWidth)))
                {
                    EditorApplication.delayCall += FetchVoices;
                }

                EditorGUILayout.Space(10);
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            foreach (var voice in voices)
            {
                if (voice.Category.Contains("premade")) { continue; }

                var isCloned = voice.Category.Contains("cloned");

                EditorGUILayout.BeginVertical();
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField($"{voice.Category}/{voice.Name} | {voice.Id}", GUILayout.MinWidth(WideColumnWidth * 2), GUILayout.ExpandWidth(true));
                    GUILayout.FlexibleSpace();

                    GUI.enabled = !isFetchingVoices;

                    if (GUILayout.Button("Delete", GUILayout.Width(DefaultColumnWidth)))
                    {
                        EditorApplication.delayCall += () => DeleteVoice(voice);
                    }

                    GUI.enabled = true;
                }
                EditorGUILayout.Space(10);
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel++;

                if (!voiceLabels.TryGetValue(voice.Id, out var cachedLabels))
                {
                    cachedLabels = new ConcurrentDictionary<string, string>();

                    foreach (var (key, value) in voice.Labels)
                    {
                        cachedLabels.TryAdd(key, value);
                    }

                    voiceLabels.TryAdd(voice.Id, cachedLabels);
                }

                EditorGUILayout.LabelField($"Labels {cachedLabels?.Count ?? 0}/5", EditorStyles.boldLabel);
                EditorGUILayout.Space();
                EditorGUI.indentLevel++;

                const string labelControlField = "labelControlField";

                if (cachedLabels is { Count: > 0 })
                {
                    var hasNewLabel = cachedLabels.ContainsKey("New Label");

                    foreach (var cachedLabel in cachedLabels)
                    {
                        if (hasNewLabel &&
                            cachedLabel.Key.Contains("New Label"))
                        {
                            continue;
                        }

                        RenderLabel(cachedLabel);
                    }

                    if (hasNewLabel)
                    {
                        RenderLabel(cachedLabels.FirstOrDefault(pair => pair.Key.Contains("New Label")));
                    }

                    void RenderLabel(KeyValuePair<string, string> label)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            var (key, value) = label;
                            var prevLabelWidth = EditorGUIUtility.labelWidth;
                            EditorGUIUtility.labelWidth = DefaultColumnWidth;
                            var keyTextControl = $"{labelControlField}_{key}";
                            EditorGUI.BeginChangeCheck();
                            GUI.SetNextControlName(keyTextControl);
                            var newKey = EditorGUILayout.TextField("Key", key);

                            if (lastTextControl == keyTextControl)
                            {
                                GUI.FocusControl(keyTextControl);
                                EditorGUI.FocusTextInControl(keyTextControl);
                                lastTextControl = string.Empty;
                            }

                            if (EditorGUI.EndChangeCheck())
                            {
                                tempLabelKey = newKey;
                            }

                            if (Event.current.isKey && Event.current.keyCode == KeyCode.Tab &&
                                GUI.GetNameOfFocusedControl() == keyTextControl &&
                                !string.IsNullOrWhiteSpace(tempLabelKey))
                            {
                                if (key != tempLabelKey)
                                {
                                    lastTextControl = $"{labelControlField}_{tempLabelKey}";

                                    EditorApplication.delayCall += () =>
                                    {
                                        if (cachedLabels.TryGetValue(key, out _))
                                        {
                                            cachedLabels.Remove(key, out _);
                                        }

                                        if (!cachedLabels.TryAdd(tempLabelKey, string.Empty))
                                        {
                                            Debug.LogError($"failed to add label {tempLabelKey}");
                                        }

                                        tempLabelKey = string.Empty;
                                        lastTextControl = $"{lastTextControl}_value";
                                        Repaint();
                                    };
                                }
                            }

                            EditorGUI.BeginChangeCheck();
                            var valueTextControl = $"{labelControlField}_{key}_value";
                            GUI.SetNextControlName(valueTextControl);
                            var newValue = EditorGUILayout.TextField("Value", value);

                            if (lastTextControl == valueTextControl)
                            {
                                GUI.FocusControl(valueTextControl);
                                EditorGUI.FocusTextInControl(valueTextControl);
                                lastTextControl = string.Empty;
                            }

                            EditorGUIUtility.labelWidth = prevLabelWidth;

                            if (EditorGUI.EndChangeCheck())
                            {
                                tempLabelValue = newValue;

                                EditorApplication.delayCall += () =>
                                {
                                    if (!string.IsNullOrWhiteSpace(tempLabelValue))
                                    {
                                        cachedLabels[key] = tempLabelValue;
                                    }

                                    tempLabelValue = string.Empty;
                                    Repaint();
                                };
                            }

                            GUI.enabled = !isFetchingVoices;

                            if (GUILayout.Button("Delete", GUILayout.Width(DefaultColumnWidth)))
                            {
                                EditorApplication.delayCall += () => { cachedLabels.Remove(key, out _); };
                            }

                            GUI.enabled = true;
                        }

                        GUILayout.Space(10);
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUILayout.Space();
                GUI.enabled = !isFetchingVoices;

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.Space(TabWidth * 2);

                    if (cachedLabels is { Count: < 5 } &&
                        !cachedLabels.ContainsKey("New Label") &&
                        GUILayout.Button("Add New Label", GUILayout.Width(WideColumnWidth)))
                    {
                        tempLabelKey = string.Empty;
                        cachedLabels.TryAdd("New Label", string.Empty);
                        lastTextControl = $"{labelControlField}_New Label";
                    }

                    GUILayout.FlexibleSpace();

                    bool IsLabelsDirty()
                    {
                        var isDirty = false;

                        foreach (var (key, cachedValue) in cachedLabels)
                        {
                            if (voice.Labels.TryGetValue(key, out var realValue))
                            {
                                isDirty |= cachedValue != realValue;
                            }
                            else
                            {
                                isDirty = true;
                            }
                        }

                        foreach (var (key, realValue) in voice.Labels)
                        {
                            if (cachedLabels.TryGetValue(key, out var cachedValue))
                            {
                                isDirty |= cachedValue != realValue;
                            }
                            else
                            {
                                isDirty = true;
                            }
                        }

                        return isDirty;
                    }

                    GUI.enabled = !isFetchingVoices && IsLabelsDirty();

                    if (GUILayout.Button("Update Labels", GUILayout.Width(DefaultColumnWidth)))
                    {
                        EditorApplication.delayCall += () =>
                        {
                            EditVoice(voice, null, cachedLabels);
                        };
                    }

                    GUILayout.Space(10);
                }
                EditorGUILayout.EndHorizontal();
                GUI.enabled = true;
                EditorGUI.indentLevel--;

                if (isCloned)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField($"Samples {voice.Samples?.Count ?? 0}/25", EditorStyles.boldLabel);
                    EditorGUILayout.Space();
                    EditorGUI.indentLevel++;

                    if ((voice.Samples?.Count ?? 0) < 25)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            //GUILayout.FlexibleSpace();
                            var thisSo = new SerializedObject(this);
                            var sampleClipsProperty = thisSo.FindProperty(nameof(newSampleClips));
                            EditorGUILayout.PropertyField(sampleClipsProperty, addNewSampleContent, true);
                            thisSo.ApplyModifiedProperties();

                            GUI.enabled = (newSampleClips?.Count > 0 && newSampleClips?.Count <= 25 - voice.Samples.Count) && !isFetchingVoices;

                            if (GUILayout.Button(addNewSampleContent, GUILayout.Width(WideColumnWidth)))
                            {
                                EditorApplication.delayCall += () =>
                                {
                                    EditVoice(voice, newSampleClips);
                                    newSampleClips = new List<AudioClip>();
                                };
                            }

                            GUI.enabled = true;
                        }
                        GUILayout.Space(10);
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUI.indentLevel--;
                }

                if (voice.Samples != null)
                {
                    EditorGUI.indentLevel++;

                    foreach (var voiceSample in voice.Samples)
                    {
                        EditorGUILayout.LabelField($"{voiceSample.Id} | {voiceSample.FileName} | {voiceSample.MimeType} | {voiceSample.SizeBytes}", GUILayout.ExpandWidth(true));
                        EditorGUILayout.BeginHorizontal();
                        var fileName = Path.GetFileNameWithoutExtension(voiceSample.FileName);
                        var files = AssetDatabase.FindAssets($"t:{nameof(AudioClip)} {fileName}").ToList();
                        files.AddRange(AssetDatabase.FindAssets($"t:{nameof(AudioClip)} {voiceSample.Id}"));

                        switch (files)
                        {
                            case { Count: 0 }:
                                GUI.enabled = !isFetchingVoices;

                                if (GUILayout.Button("Download", GUILayout.Width(DefaultColumnWidth)))
                                {
                                    EditorApplication.delayCall += () => DownloadVoiceSample(voice, voiceSample);
                                }

                                GUI.enabled = true;
                                break;
                            case { Count: 1 }:
                                var clipPath = AssetDatabase.GUIDToAssetPath(files[0]);
                                var sampleClip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                                EditorGUILayout.ObjectField(GUIContent.none, sampleClip, typeof(AudioClip), false);
                                break;
                            default:
                                EditorGUILayout.LabelField($"Found multiple matches for {fileName}");
                                break;
                        }

                        GUI.enabled = !isFetchingVoices;

                        if (GUILayout.Button("Delete", GUILayout.Width(DefaultColumnWidth)))
                        {
                            EditorApplication.delayCall += () => DeleteVoiceSample(voice, voiceSample);
                        }

                        GUI.enabled = true;
                        GUILayout.Space(10);
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
        }

        private void ShowCreateVoiceEditorPopup()
        {
        }

        private static async void DeleteVoice(Voice voice)
        {
            if (!EditorUtility.DisplayDialog(
                    "Alert!",
                    $"Are you sure you want to delete voice {voice.Id} \"{voice.Name}\"?", "Yes",
                    "No"))
            {
                return;
            }

            try
            {
                var result = await api.VoicesEndpoint.DeleteVoiceAsync(voice);

                if (!result)
                {
                    Debug.LogError($"Failed to delete voice: {voice.Name}!");
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                FetchVoices();
            }
        }

        private static async void EditVoice(Voice voice, List<AudioClip> audioClips = null, IReadOnlyDictionary<string, string> labels = null)
        {
            try
            {
                var audioClipPaths = new List<string>();

                if (audioClips != null)
                {
                    EditorUtility.DisplayProgressBar("Uploading voice sample...", $"Uploading {audioClips.Count} voice samples: {string.Join(", ", audioClips.Select(clip => clip.name))}", -1);

                    foreach (var audioClip in audioClips)
                    {
                        var audioClipPath = Path.GetFullPath(AssetDatabase.GetAssetPath(audioClip));

                        if (string.IsNullOrWhiteSpace(audioClipPath))
                        {
                            throw new ArgumentNullException(nameof(audioClipPath), $"AssetDatabase failed to locate {audioClip.name}!");
                        }

                        if (!File.Exists(audioClipPath))
                        {
                            throw new ArgumentNullException(nameof(audioClipPath), $"Failed to find valid path to {audioClip.name}: \"{audioClipPath}\"");
                        }

                        audioClipPaths.Add(audioClipPath);
                    }
                }
                else
                {
                    EditorUtility.DisplayProgressBar("Updating voice labels...", $"Updating voice labels for {voice.Id}", -1);

                    if (labels == null)
                    {
                        throw new ArgumentNullException(nameof(labels));
                    }
                }

                var result = await api.VoicesEndpoint.EditVoiceAsync(voice, audioClipPaths, labels);

                if (!result)
                {
                    Debug.LogWarning("Failed to update voice!");
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                FetchVoices();
            }
        }

        private static async void DeleteVoiceSample(Voice voice, Sample voiceSample)
        {
            if (!EditorUtility.DisplayDialog(
                    "Alert!",
                    $"Are you sure you want to delete sample {voiceSample.Id} from {voice.Name}?", "Yes",
                    "No"))
            {
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Deleting voice sample...", $"Deleting {voiceSample.Id}", -1);
                await api.VoicesEndpoint.DeleteVoiceSampleAsync(voice, voiceSample);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                FetchVoices();
            }
        }

        private static async void DownloadVoiceSample(Voice sample, Sample voiceSample)
        {
            try
            {
                await api.VoicesEndpoint.GetVoiceSampleAsync(sample, voiceSample, editorDownloadDirectory);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
        }

        #endregion Voice Lab

        #region History

        private static void RenderHistory()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();

            EditorGUILayout.BeginHorizontal();
            { //Header
                EditorGUILayout.LabelField("History", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                GUI.enabled = !isFetchingVoices || !isFetchingUserInfo;

                if (GUILayout.Button("Refresh", GUILayout.Width(DefaultColumnWidth)))
                {
                    EditorApplication.delayCall += FetchHistory;
                }

                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Space(TabWidth);

                GUI.enabled = !isFetchingHistory && history.Count != downloadedClips.Count;

                if (GUILayout.Button("Download All History", GUILayout.ExpandWidth(true)))
                {
                    EditorApplication.delayCall += () => DownloadHistoryAudio(history);
                }

                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            foreach (var item in history)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{item.Date} | {item.VoiceName}");

                GUI.enabled = !downloadedClips.Any(path =>
                {
                    var assetPath = Path.GetFileNameWithoutExtension(path);
                    return assetPath == item.Id || assetPath == item.TextHash;
                });

                if (GUILayout.Button("Download", GUILayout.Width(DefaultColumnWidth)))
                {
                    EditorApplication.delayCall += () => DownloadHistoryAudio(new[] { item });
                }

                GUI.enabled = !isFetchingHistory;

                if (GUILayout.Button("Delete", GUILayout.Width(DefaultColumnWidth)))
                {
                    EditorApplication.delayCall += () => DeleteHistoryItem(item);
                }

                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();

                GUI.enabled = false;
                EditorGUILayout.TextArea(item.Text);
                GUI.enabled = true;
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
            EditorGUILayout.EndHorizontal();
        }

        private static async void DownloadHistoryAudio(IEnumerable<HistoryItem> items)
        {
            var historyItemsToDownload = items.Select(item => item.Id).ToList();

            foreach (var clipPath in downloadedClips)
            {
                var clipName = Path.GetFileNameWithoutExtension(clipPath);

                if (historyItemsToDownload.Contains(clipName))
                {
                    historyItemsToDownload.Remove(clipName);
                }
            }

            if (historyItemsToDownload.Count > 0)
            {
                try
                {
                    EditorUtility.DisplayProgressBar("Downloading history...", $"Downloading {historyItemsToDownload.Count} items...", -1);
                    await api.HistoryEndpoint.DownloadHistoryItemsAsync(historyItemsToDownload, editorDownloadDirectory);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                    FetchHistory();
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                }
            }
        }

        private static async void DeleteHistoryItem(HistoryItem item)
        {
            if (!EditorUtility.DisplayDialog(
                    "Alert!",
                    $"Are you sure you want to delete history {item.Id}?", "Yes",
                    "No"))
            {
                return;
            }

            try
            {
                await api.HistoryEndpoint.DeleteHistoryItemAsync(item);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                FetchHistory();
            }
        }

        #endregion History

        #region GUI Utilities

        private static void StartIndent(int levels)
        {
            for (var i = 0; i < levels; i++)
            {
                EditorGUI.indentLevel++;
            }
        }

        private static void EndIndent(int levels)
        {
            for (var i = 0; i < levels; i++)
            {
                EditorGUI.indentLevel--;
            }
        }

        #endregion GUI Utilities
    }
}
