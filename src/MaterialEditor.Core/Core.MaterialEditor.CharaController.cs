﻿using ExtensibleSaveFormat;
using Newtonsoft.Json;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Maker;
using MaterialEditorAPI;
using MessagePack;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UniRx;
using UnityEngine;
using static MaterialEditorAPI.MaterialAPI;
using static MaterialEditorAPI.MaterialEditorPluginBase;
using KKAPI.Utilities;
#if AI || HS2
using AIChara;
#endif
#if PH
using ChaFileCoordinate = Character.CustomParameter;
using ChaControl = Human;
#endif

namespace KK_Plugins.MaterialEditor
{
    using MEAnimationController = MEAnimationController<MaterialEditorCharaController, MaterialEditorCharaController.MaterialTextureProperty>;

    /// <summary>
    /// KKAPI character controller that handles saving and loading character data as well as provides methods to get or set the saved data
    /// </summary>
    public class MaterialEditorCharaController : CharaCustomFunctionController
    {
        private readonly List<RendererProperty> RendererPropertyList = new List<RendererProperty>();
        private readonly List<ProjectorProperty> ProjectorPropertyList = new List<ProjectorProperty>();
        private readonly List<MaterialNameProperty> MaterialNamePropertyList = new List<MaterialNameProperty>();
        private readonly List<MaterialFloatProperty> MaterialFloatPropertyList = new List<MaterialFloatProperty>();
        private readonly List<MaterialColorProperty> MaterialColorPropertyList = new List<MaterialColorProperty>();
        private readonly List<MaterialKeywordProperty> MaterialKeywordPropertyList = new List<MaterialKeywordProperty>();
        private readonly List<MaterialTextureProperty> MaterialTexturePropertyList = new List<MaterialTextureProperty>();
        private readonly List<MaterialShader> MaterialShaderList = new List<MaterialShader>();
        private readonly List<MaterialCopy> MaterialCopyList = new List<MaterialCopy>();

        private readonly Dictionary<int, TextureContainer> TextureDictionary = new Dictionary<int, TextureContainer>();

        private readonly Dictionary<MaterialTextureProperty, MEAnimationController> AnimationControllerMap = new Dictionary<MaterialTextureProperty, MEAnimationController>();

        static MaterialEditorCharaController()
        {
            InitAnimationController();
        }

        /// <summary>
        /// Index of the currently worn coordinate. Always 0 except for in Koikatsu
        /// </summary>
#if KK || KKS
        public int CurrentCoordinateIndex => ChaControl.fileStatus.coordinateType;
#else
        public int CurrentCoordinateIndex => 0;
#endif
        private string FileToSet;
        private string PropertyToSet;
        private Material MatToSet;
        private int SlotToSet;
        private ObjectType ObjectTypeToSet;
        private GameObject GameObjectToSet;

        /// <summary>
        /// Handles saving data to character cards
        /// </summary>
        /// <param name="currentGameMode"></param>
        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
#if KK || KKS
            //Always run on save to also purge them for cards made before this purging was implemented
            PurgeUnusedCoordinates();
#endif
            PurgeUnusedTextures();

            if (RendererPropertyList.Count == 0 && MaterialFloatPropertyList.Count == 0 && MaterialKeywordPropertyList.Count == 0 && MaterialColorPropertyList.Count == 0 && MaterialTexturePropertyList.Count == 0 && MaterialShaderList.Count == 0 && MaterialCopyList.Count == 0)
            {
                SetExtendedData(null);
            }
            else
            {
                var data = new PluginData();

                if (TextureDictionary.Count > 0)
                    data.data.Add(nameof(TextureDictionary), MessagePackSerializer.Serialize(TextureDictionary.ToDictionary(pair => pair.Key, pair => pair.Value.Data)));
                else
                    data.data.Add(nameof(TextureDictionary), null);

                if (RendererPropertyList.Count > 0)
                    data.data.Add(nameof(RendererPropertyList), MessagePackSerializer.Serialize(RendererPropertyList));
                else
                    data.data.Add(nameof(RendererPropertyList), null);

                if (ProjectorPropertyList.Count > 0)
                    data.data.Add(nameof(ProjectorPropertyList), MessagePackSerializer.Serialize(ProjectorPropertyList));
                else
                    data.data.Add(nameof(ProjectorPropertyList), null);

                if (MaterialNamePropertyList.Count > 0)
                    data.data.Add(nameof(MaterialNamePropertyList), MessagePackSerializer.Serialize(MaterialNamePropertyList));
                else
                    data.data.Add(nameof(MaterialNamePropertyList), null);

                if (MaterialFloatPropertyList.Count > 0)
                    data.data.Add(nameof(MaterialFloatPropertyList), MessagePackSerializer.Serialize(MaterialFloatPropertyList));
                else
                    data.data.Add(nameof(MaterialFloatPropertyList), null);

                if (MaterialKeywordPropertyList.Count > 0)
                    data.data.Add(nameof(MaterialKeywordPropertyList), MessagePackSerializer.Serialize(MaterialKeywordPropertyList));
                else
                    data.data.Add(nameof(MaterialKeywordPropertyList), null);

                if (MaterialColorPropertyList.Count > 0)
                    data.data.Add(nameof(MaterialColorPropertyList), MessagePackSerializer.Serialize(MaterialColorPropertyList));
                else
                    data.data.Add(nameof(MaterialColorPropertyList), null);

                if (MaterialTexturePropertyList.Count > 0)
                    data.data.Add(nameof(MaterialTexturePropertyList), MessagePackSerializer.Serialize(MaterialTexturePropertyList));
                else
                    data.data.Add(nameof(MaterialTexturePropertyList), null);

                if (MaterialShaderList.Count > 0)
                    data.data.Add(nameof(MaterialShaderList), MessagePackSerializer.Serialize(MaterialShaderList));
                else
                    data.data.Add(nameof(MaterialShaderList), null);

                if (MaterialCopyList.Count > 0)
                    data.data.Add(nameof(MaterialCopyList), MessagePackSerializer.Serialize(MaterialCopyList));
                else
                    data.data.Add(nameof(MaterialCopyList), null);

                SetExtendedData(data);
            }
        }

        /// <summary>
        /// Handles loading data from character cards
        /// </summary>
        /// <param name="currentGameMode"></param>
        /// <param name="maintainState"></param>
        protected override void OnReload(GameMode currentGameMode, bool maintainState)
        {
            if (!maintainState)
            {
                RemoveMaterialCopies(ChaControl.gameObject);

                CharacterLoading = true;
                LoadCharacterExtSaveData();
            }

            ChaControl.StartCoroutine(LoadData(true, true, true));
        }

        internal new void Update()
        {
            SetMaterialTextureFromFileByUpdate();
            MEAnimationController.UpdateAnimations(AnimationControllerMap);
            base.Update();
            if (MaterialEditorPlugin.PurgeOrphanedPropertiesHotkey.Value.IsDown())
                PurgeOrphanedProperties();
            if (MakerAPI.InsideMaker)
            {
                if (MaterialEditorPlugin.DisableShadowCastingHotkey.Value.IsDown())
                {
                    SetRendererPropertyRecursive(RendererProperties.ShadowCastingMode, "0");
                    MaterialEditorPlugin.Logger.LogMessage($"Disabled ShadowCasting");
                }
                else if (MaterialEditorPlugin.EnableShadowCastingHotkey.Value.IsDown())
                {
                    SetRendererPropertyRecursive(RendererProperties.ShadowCastingMode, "1");
                    MaterialEditorPlugin.Logger.LogMessage($"Enabled ShadowCasting");
                }
                else if (MaterialEditorPlugin.TwoSidedShadowCastingHotkey.Value.IsDown())
                {
                    SetRendererPropertyRecursive(RendererProperties.ShadowCastingMode, "2");
                    MaterialEditorPlugin.Logger.LogMessage($"Two Sided ShadowCasting");
                }
                else if (MaterialEditorPlugin.ShadowsOnlyShadowCastingHotkey.Value.IsDown())
                {
                    SetRendererPropertyRecursive(RendererProperties.ShadowCastingMode, "3");
                    MaterialEditorPlugin.Logger.LogMessage($"Shadows Only ShadowCasting");
                }
                else if (MaterialEditorPlugin.ResetShadowCastingHotkey.Value.IsDown())
                {
                    SetRendererPropertyRecursive(RendererProperties.ShadowCastingMode, "-1");
                    MaterialEditorPlugin.Logger.LogMessage($"Reset ShadowCasting ShadowCasting");
                }
                else if (MaterialEditorPlugin.DisableReceiveShadows.Value.IsDown())
                {
                    SetRendererPropertyRecursive(RendererProperties.ReceiveShadows, "0");
                    MaterialEditorPlugin.Logger.LogMessage($"Disabled ReceiveShadows");
                }
                else if (MaterialEditorPlugin.EnableReceiveShadows.Value.IsDown())
                {
                    SetRendererPropertyRecursive(RendererProperties.ReceiveShadows, "1");
                    MaterialEditorPlugin.Logger.LogMessage($"Enabled ReceiveShadows");
                }
                else if (MaterialEditorPlugin.ResetReceiveShadows.Value.IsDown())
                {
                    SetRendererPropertyRecursive(RendererProperties.ReceiveShadows, "-1");
                    MaterialEditorPlugin.Logger.LogMessage($"Reset ReceiveShadows");
                }
            }
        }

        internal void SetRendererPropertyRecursive(RendererProperties property, string value, bool affectBody = false)
        {
            if (affectBody)
                foreach (var rend in GetRendererList(ChaControl.gameObject))
                {
                    //Disable the shadowcaster renderer instead of changing the shadowcasting mode
                    if (property == RendererProperties.ShadowCastingMode && (rend.name == "o_shadowcaster" || rend.name == "o_shadowcaster_cm"))
                    {
                        if (value == "-1")
                            RemoveRendererProperty(0, MaterialEditorCharaController.ObjectType.Character, rend, RendererProperties.Enabled, ChaControl.gameObject);
                        //keep consistency in the casted shadow with how it would normally look
                        else if (value == "2" | value == "3")
                            {
                                RemoveRendererProperty(0, MaterialEditorCharaController.ObjectType.Character, rend, RendererProperties.Enabled, ChaControl.gameObject);
                                SetRendererProperty(0, MaterialEditorCharaController.ObjectType.Character, rend, property, value, ChaControl.gameObject);
                            }
                            else
                                SetRendererProperty(0, MaterialEditorCharaController.ObjectType.Character, rend, RendererProperties.Enabled, value, ChaControl.gameObject);
                    }
                    else
                    {
                        if (value == "-1")
                            RemoveRendererProperty(0, MaterialEditorCharaController.ObjectType.Character, rend, property, ChaControl.gameObject);
                        else
                            SetRendererProperty(0, MaterialEditorCharaController.ObjectType.Character, rend, property, value, ChaControl.gameObject);
                    }
                }
            var clothes = ChaControl.GetClothes();
            for (var i = 0; i < clothes.Length; i++)
            {
                var gameObj = clothes[i];
                foreach (var renderer in GetRendererList(gameObj))
                    if (value == "-1")
                        RemoveRendererProperty(i, MaterialEditorCharaController.ObjectType.Clothing, renderer, property, gameObj);
                    else
                        SetRendererProperty(i, MaterialEditorCharaController.ObjectType.Clothing, renderer, property, value, gameObj);
            }
            var hair = ChaControl.GetHair();
            for (var i = 0; i < hair.Length; i++)
            {
                var gameObj = hair[i];
                foreach (var renderer in GetRendererList(gameObj))
                    if (value == "-1")
                        RemoveRendererProperty(i, MaterialEditorCharaController.ObjectType.Hair, renderer, property, gameObj);
                    else
                        SetRendererProperty(i, MaterialEditorCharaController.ObjectType.Hair, renderer, property, value, gameObj);
            }
            var accessories = ChaControl.GetAccessoryObjects();
            for (var i = 0; i < accessories.Length; i++)
            {
                var gameObj = accessories[i];
                if (gameObj != null)
                    foreach (var renderer in GetRendererList(gameObj))
                        if (value == "-1")
                            RemoveRendererProperty(i, MaterialEditorCharaController.ObjectType.Accessory, renderer, property, gameObj);
                        else
                            SetRendererProperty(i, MaterialEditorCharaController.ObjectType.Accessory, renderer, property, value, gameObj);
            }
        }

        /// <summary>
        /// Used by SetMaterialTextureFromFile if setTexInUpdate is true, needed for loading files via file dialogue
        /// </summary>
        private void SetMaterialTextureFromFileByUpdate()
        {
            try
            {
                if (FileToSet != null)
                    SetMaterialTextureFromFile(SlotToSet, ObjectTypeToSet, MatToSet, PropertyToSet, FileToSet, GameObjectToSet);
            }
            catch
            {
                //MaterialEditorPlugin.Logger.Log(BepInEx.Logging.LogLevel.Error | BepInEx.Logging.LogLevel.Message, "Failed to load texture.");
            }
            finally
            {
                FileToSet = null;
                PropertyToSet = null;
                MatToSet = null;
                GameObjectToSet = null;
            }
        }

        /// <summary>
        /// Handles saving data to coordinate cards
        /// </summary>
        /// <param name="coordinate"></param>
        protected override void OnCoordinateBeingSaved(ChaFileCoordinate coordinate)
        {

            var coordinateRendererPropertyList = RendererPropertyList.Where(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType != ObjectType.Hair && x.ObjectType != ObjectType.Character).ToList();
            var coordinateProjectorPropertyList = ProjectorPropertyList.Where(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType != ObjectType.Hair && x.ObjectType != ObjectType.Character).ToList();
            var coordinateMaterialNamePropertyList = MaterialNamePropertyList.Where(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType != ObjectType.Hair && x.ObjectType != ObjectType.Character).ToList();
            var coordinateMaterialFloatPropertyList = MaterialFloatPropertyList.Where(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType != ObjectType.Hair && x.ObjectType != ObjectType.Character).ToList();
            var coordinateMaterialKeywordPropertyList = MaterialKeywordPropertyList.Where(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType != ObjectType.Hair && x.ObjectType != ObjectType.Character).ToList();
            var coordinateMaterialColorPropertyList = MaterialColorPropertyList.Where(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType != ObjectType.Hair && x.ObjectType != ObjectType.Character).ToList();
            var coordinateMaterialTexturePropertyList = MaterialTexturePropertyList.Where(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType != ObjectType.Hair && x.ObjectType != ObjectType.Character).ToList();
            var coordinateMaterialShaderList = MaterialShaderList.Where(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType != ObjectType.Hair && x.ObjectType != ObjectType.Character).ToList();
            var coordinateMaterialCopyList = MaterialCopyList.Where(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType != ObjectType.Hair && x.ObjectType != ObjectType.Character).ToList();
            var coordinateTextureDictionary = new Dictionary<int, byte[]>();

            var usedTexIDMap = MEAnimationController.GetUsedTexIDSet(AnimationControllerMap, coordinateMaterialTexturePropertyList);

            foreach (var tex in TextureDictionary)
            {
                if (usedTexIDMap.Contains(tex.Key))
                    coordinateTextureDictionary.Add(tex.Key, tex.Value.Data);
            }

            if (coordinateRendererPropertyList.Count == 0 && coordinateMaterialNamePropertyList.Count == 0 && coordinateMaterialFloatPropertyList.Count == 0 && coordinateMaterialKeywordPropertyList.Count == 0 && coordinateMaterialColorPropertyList.Count == 0 && coordinateMaterialTexturePropertyList.Count == 0 && coordinateMaterialShaderList.Count == 0 && coordinateMaterialCopyList.Count == 0)
            {
                SetCoordinateExtendedData(coordinate, null);
            }
            else
            {
                var data = new PluginData();
                if (coordinateTextureDictionary.Count > 0)
                    data.data.Add(nameof(TextureDictionary), MessagePackSerializer.Serialize(coordinateTextureDictionary));
                else
                    data.data.Add(nameof(TextureDictionary), null);

                if (coordinateRendererPropertyList.Count > 0)
                    data.data.Add(nameof(RendererPropertyList), MessagePackSerializer.Serialize(coordinateRendererPropertyList));
                else
                    data.data.Add(nameof(RendererPropertyList), null);

                if (coordinateProjectorPropertyList.Count > 0)
                    data.data.Add(nameof(ProjectorPropertyList), MessagePackSerializer.Serialize(coordinateProjectorPropertyList));
                else
                    data.data.Add(nameof(ProjectorPropertyList), null);

                if (coordinateMaterialNamePropertyList.Count > 0)
                    data.data.Add(nameof(MaterialNamePropertyList), MessagePackSerializer.Serialize(coordinateMaterialNamePropertyList));
                else
                    data.data.Add(nameof(MaterialNamePropertyList), null);

                if (coordinateMaterialFloatPropertyList.Count > 0)
                    data.data.Add(nameof(MaterialFloatPropertyList), MessagePackSerializer.Serialize(coordinateMaterialFloatPropertyList));
                else
                    data.data.Add(nameof(MaterialFloatPropertyList), null);

                if (coordinateMaterialKeywordPropertyList.Count > 0)
                    data.data.Add(nameof(MaterialKeywordPropertyList), MessagePackSerializer.Serialize(coordinateMaterialKeywordPropertyList));
                else
                    data.data.Add(nameof(MaterialKeywordPropertyList), null);

                if (coordinateMaterialColorPropertyList.Count > 0)
                    data.data.Add(nameof(MaterialColorPropertyList), MessagePackSerializer.Serialize(coordinateMaterialColorPropertyList));
                else
                    data.data.Add(nameof(MaterialColorPropertyList), null);

                if (coordinateMaterialTexturePropertyList.Count > 0)
                    data.data.Add(nameof(MaterialTexturePropertyList), MessagePackSerializer.Serialize(coordinateMaterialTexturePropertyList));
                else
                    data.data.Add(nameof(MaterialTexturePropertyList), null);

                if (coordinateMaterialShaderList.Count > 0)
                    data.data.Add(nameof(MaterialShaderList), MessagePackSerializer.Serialize(coordinateMaterialShaderList));
                else
                    data.data.Add(nameof(MaterialShaderList), null);

                if (coordinateMaterialCopyList.Count > 0)
                    data.data.Add(nameof(MaterialCopyList), MessagePackSerializer.Serialize(coordinateMaterialCopyList));
                else
                    data.data.Add(nameof(MaterialCopyList), null);

                SetCoordinateExtendedData(coordinate, data);
            }

            base.OnCoordinateBeingSaved(coordinate);
        }

        /// <summary>
        /// Handles loading data from coordinate cards
        /// </summary>
        /// <param name="coordinate"></param>
        /// <param name="maintainState"></param>
        protected override void OnCoordinateBeingLoaded(ChaFileCoordinate coordinate, bool maintainState)
        {
            LoadCoordinateExtSaveData(coordinate);

            CoordinateChanging = true;

            if (MakerAPI.InsideAndLoaded)
                MaterialEditorUI.Visible = false;

            ChaControl.StartCoroutine(LoadData(true, true, false));
            base.OnCoordinateBeingLoaded(coordinate, maintainState);
        }

        private void LoadCharacterExtSaveData()
        {
            RemoveMaterialCopies(ChaControl.gameObject);

            List<ObjectType> objectTypesToLoad = new List<ObjectType>();

            var loadFlags = MakerAPI.GetCharacterLoadFlags();
            if (loadFlags == null)
            {
                RendererPropertyList.Clear();
                ProjectorPropertyList.Clear();
                MaterialNamePropertyList.Clear();
                MaterialFloatPropertyList.Clear();
                MaterialKeywordPropertyList.Clear();
                MaterialColorPropertyList.Clear();
                MaterialTexturePropertyList.Clear();
                MaterialShaderList.Clear();
                MaterialCopyList.Clear();
                AnimationControllerMap.Clear();

                objectTypesToLoad.Add(ObjectType.Accessory);
                objectTypesToLoad.Add(ObjectType.Character);
                objectTypesToLoad.Add(ObjectType.Clothing);
                objectTypesToLoad.Add(ObjectType.Hair);
            }
            else
            {
                bool changed = false;

                if (loadFlags.Face || loadFlags.Body)
                {
                    RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Character);
                    ProjectorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Character);
                    MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Character);
                    MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Character);
                    MaterialKeywordPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Character);
                    MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Character);
                    MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Character);
                    MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Character);
                    MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Character);

                    objectTypesToLoad.Add(ObjectType.Character);

                    changed = true;
                }
                if (loadFlags.Clothes)
                {
                    RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing);
                    ProjectorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing);
                    MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing);
                    MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing);
                    MaterialKeywordPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing);
                    MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing);
                    MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing);
                    MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Clothing);
                    MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing);
                    objectTypesToLoad.Add(ObjectType.Clothing);

                    RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory);
                    ProjectorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory);
                    MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory);
                    MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory);
                    MaterialKeywordPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory);
                    MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory);
                    MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory);
                    MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Accessory);
                    MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory);
                    objectTypesToLoad.Add(ObjectType.Accessory);

                    changed = true;
                }
                if (loadFlags.Hair)
                {
                    RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair);
                    ProjectorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair);
                    MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair);
                    MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair);
                    MaterialKeywordPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair);
                    MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair);
                    MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair);
                    MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Hair);
                    MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Hair);
                    objectTypesToLoad.Add(ObjectType.Hair);

                    changed = true;
                }

                if (changed)
                {
                    PurgeUnusedAnimation();
                }
            }

            //Don't destroy the textures in H mode because they will still be needed
            if (KoikatuAPI.GetCurrentGameMode() != GameMode.MainGame)
            {
                PurgeUnusedTextures();
            }

            CharacterLoading = true;

            var data = GetExtendedData();
            if (data != null)
            {
                var importDictionary = new Dictionary<int, int>();

                if (data.data.TryGetValue(nameof(TextureDictionary), out var texDic) && texDic != null)
                    foreach (var x in MessagePackSerializer.Deserialize<Dictionary<int, byte[]>>((byte[])texDic))
                        importDictionary[x.Key] = SetAndGetTextureID(x.Value);

                //Debug for dumping all textures
                //int counter = 1;
                //foreach (var tex in TextureDictionary.Values)
                //{
                //    string filename = Path.Combine(MaterialEditorPlugin.ExportPath, $"_Export_{ChaControl.GetCharacterName()}_{counter}.png");
                //    MaterialEditorPlugin.SaveTex(tex.Texture, filename);
                //    MaterialEditorPlugin.Logger.LogInfo($"Exported {filename}");
                //    counter++;
                //}

                if (data.data.TryGetValue(nameof(MaterialShaderList), out var shaderProperties) && shaderProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialShader>>((byte[])shaderProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        int coordinateIndex = loadedProperty.ObjectType == ObjectType.Character ? 0 : loadedProperty.CoordinateIndex;
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            MaterialShaderList.Add(new MaterialShader(loadedProperty.ObjectType, coordinateIndex, loadedProperty.Slot, loadedProperty.MaterialName, loadedProperty.ShaderName, loadedProperty.ShaderNameOriginal, loadedProperty.RenderQueue, loadedProperty.RenderQueueOriginal));
                    }
                }

                if (data.data.TryGetValue(nameof(RendererPropertyList), out var rendererProperties) && rendererProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<RendererProperty>>((byte[])rendererProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        int coordinateIndex = loadedProperty.ObjectType == ObjectType.Character ? 0 : loadedProperty.CoordinateIndex;
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            RendererPropertyList.Add(new RendererProperty(loadedProperty.ObjectType, coordinateIndex, loadedProperty.Slot, loadedProperty.RendererName, loadedProperty.Property, loadedProperty.Value, loadedProperty.ValueOriginal));
                    }
                }

                if (data.data.TryGetValue(nameof(ProjectorPropertyList), out var projectorProperties) && projectorProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<ProjectorProperty>>((byte[])projectorProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        int coordinateIndex = loadedProperty.ObjectType == ObjectType.Character ? 0 : loadedProperty.CoordinateIndex;
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            ProjectorPropertyList.Add(new ProjectorProperty(loadedProperty.ObjectType, coordinateIndex, loadedProperty.Slot, loadedProperty.ProjectorName, loadedProperty.Property, loadedProperty.Value, loadedProperty.ValueOriginal));
                    }
                }

                if (data.data.TryGetValue(nameof(MaterialNamePropertyList), out var materialNameProperties) && materialNameProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialNameProperty>>((byte[])materialNameProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        int coordinateIndex = loadedProperty.ObjectType == ObjectType.Character ? 0 : loadedProperty.CoordinateIndex;
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            MaterialNamePropertyList.Add(new MaterialNameProperty(loadedProperty.ObjectType, coordinateIndex, loadedProperty.Slot, loadedProperty.Renderer, loadedProperty.MaterialName, loadedProperty.Value));
                    }
                }

                if (data.data.TryGetValue(nameof(MaterialFloatPropertyList), out var materialFloatProperties) && materialFloatProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialFloatProperty>>((byte[])materialFloatProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        int coordinateIndex = loadedProperty.ObjectType == ObjectType.Character ? 0 : loadedProperty.CoordinateIndex;
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            MaterialFloatPropertyList.Add(new MaterialFloatProperty(loadedProperty.ObjectType, coordinateIndex, loadedProperty.Slot, loadedProperty.MaterialName, loadedProperty.Property, loadedProperty.Value, loadedProperty.ValueOriginal));
                    }
                }

                if (data.data.TryGetValue(nameof(MaterialKeywordPropertyList), out var materialKeywordProperties) && materialKeywordProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialKeywordProperty>>((byte[])materialKeywordProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        int coordinateIndex = loadedProperty.ObjectType == ObjectType.Character ? 0 : loadedProperty.CoordinateIndex;
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            MaterialKeywordPropertyList.Add(new MaterialKeywordProperty(loadedProperty.ObjectType, coordinateIndex, loadedProperty.Slot, loadedProperty.MaterialName, loadedProperty.Property, loadedProperty.Value, loadedProperty.ValueOriginal));
                    }
                }

                if (data.data.TryGetValue(nameof(MaterialColorPropertyList), out var materialColorProperties) && materialColorProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialColorProperty>>((byte[])materialColorProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        int coordinateIndex = loadedProperty.ObjectType == ObjectType.Character ? 0 : loadedProperty.CoordinateIndex;
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            MaterialColorPropertyList.Add(new MaterialColorProperty(loadedProperty.ObjectType, coordinateIndex, loadedProperty.Slot, loadedProperty.MaterialName, loadedProperty.Property, loadedProperty.Value, loadedProperty.ValueOriginal));
                    }
                }

                if (data.data.TryGetValue(nameof(MaterialTexturePropertyList), out var materialTextureProperties) && materialTextureProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialTextureProperty>>((byte[])materialTextureProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType) && !loadedProperty.NullCheck())
                        {
                            int? texID = null;
                            if (loadedProperty.TexID != null && importDictionary.TryGetValue((int)loadedProperty.TexID, out var importTextID))
                                texID = importTextID;
                            MEAnimationUtil.RemapTexID(loadedProperty.TexAnimationDef, importDictionary);
                            int coordinateIndex = loadedProperty.ObjectType == ObjectType.Character ? 0 : loadedProperty.CoordinateIndex;
                            MaterialTextureProperty newTextureProperty = new MaterialTextureProperty(loadedProperty.ObjectType, coordinateIndex, loadedProperty.Slot, loadedProperty.MaterialName, loadedProperty.Property, texID, loadedProperty.Offset, loadedProperty.OffsetOriginal, loadedProperty.Scale, loadedProperty.ScaleOriginal, loadedProperty.TexAnimationDef);
                            MaterialTexturePropertyList.Add(newTextureProperty);
                        }
                    }
                }

                if (data.data.TryGetValue(nameof(MaterialCopyList), out var materialCopyData) && materialCopyData != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialCopy>>((byte[])materialCopyData);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        int coordinateIndex = loadedProperty.ObjectType == ObjectType.Character ? 0 : loadedProperty.CoordinateIndex;
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            MaterialCopyList.Add(new MaterialCopy(loadedProperty.ObjectType, coordinateIndex, loadedProperty.Slot, loadedProperty.MaterialName, loadedProperty.MaterialCopyName));
                    }
                }
            }
        }

        private void LoadCoordinateExtSaveData(ChaFileCoordinate coordinate)
        {
            List<ObjectType> objectTypesToLoad = new List<ObjectType>();

            var loadFlags = MakerAPI.GetCoordinateLoadFlags();
            if (loadFlags == null)
            {
                RendererPropertyList.RemoveAll(x => (x.ObjectType == ObjectType.Clothing || x.ObjectType == ObjectType.Accessory) && x.CoordinateIndex == CurrentCoordinateIndex);
                MaterialNamePropertyList.RemoveAll(x => (x.ObjectType == ObjectType.Clothing || x.ObjectType == ObjectType.Accessory) && x.CoordinateIndex == CurrentCoordinateIndex);
                MaterialFloatPropertyList.RemoveAll(x => (x.ObjectType == ObjectType.Clothing || x.ObjectType == ObjectType.Accessory) && x.CoordinateIndex == CurrentCoordinateIndex);
                MaterialKeywordPropertyList.RemoveAll(x => (x.ObjectType == ObjectType.Clothing || x.ObjectType == ObjectType.Accessory) && x.CoordinateIndex == CurrentCoordinateIndex);
                MaterialColorPropertyList.RemoveAll(x => (x.ObjectType == ObjectType.Clothing || x.ObjectType == ObjectType.Accessory) && x.CoordinateIndex == CurrentCoordinateIndex);
                MaterialTexturePropertyList.RemoveAll(x => (x.ObjectType == ObjectType.Clothing || x.ObjectType == ObjectType.Accessory) && x.CoordinateIndex == CurrentCoordinateIndex);
                MaterialShaderList.RemoveAll(x => (x.ObjectType == ObjectType.Clothing || x.ObjectType == ObjectType.Accessory) && x.CoordinateIndex == CurrentCoordinateIndex);
                MaterialCopyList.RemoveAll(x => (x.ObjectType == ObjectType.Clothing || x.ObjectType == ObjectType.Accessory) && x.CoordinateIndex == CurrentCoordinateIndex);

                objectTypesToLoad.Add(ObjectType.Accessory);
                objectTypesToLoad.Add(ObjectType.Clothing);

                PurgeUnusedAnimation();
            }
            else
            {
                if (loadFlags.Clothes)
                {
                    RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialKeywordPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex);
                    objectTypesToLoad.Add(ObjectType.Clothing);
                }
                if (loadFlags.Accessories)
                {
                    RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialKeywordPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex);
                    MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex);
                    objectTypesToLoad.Add(ObjectType.Accessory);
                }

                if (loadFlags.Clothes || loadFlags.Accessories)
                {
                    PurgeUnusedAnimation();
                }
            }

            var data = GetCoordinateExtendedData(coordinate);
            if (data?.data != null)
            {
                var importDictionary = new Dictionary<int, int>();

                if (data.data.TryGetValue(nameof(TextureDictionary), out var texDic) && texDic != null)
                    foreach (var x in MessagePackSerializer.Deserialize<Dictionary<int, byte[]>>((byte[])texDic))
                        importDictionary[x.Key] = SetAndGetTextureID(x.Value);

                if (data.data.TryGetValue(nameof(MaterialShaderList), out var materialShaders) && materialShaders != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialShader>>((byte[])materialShaders);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            MaterialShaderList.Add(new MaterialShader(loadedProperty.ObjectType, CurrentCoordinateIndex, loadedProperty.Slot, loadedProperty.MaterialName, loadedProperty.ShaderName, loadedProperty.ShaderNameOriginal, loadedProperty.RenderQueue, loadedProperty.RenderQueueOriginal));
                    }
                }

                if (data.data.TryGetValue(nameof(RendererPropertyList), out var rendererProperties) && rendererProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<RendererProperty>>((byte[])rendererProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            RendererPropertyList.Add(new RendererProperty(loadedProperty.ObjectType, CurrentCoordinateIndex, loadedProperty.Slot, loadedProperty.RendererName, loadedProperty.Property, loadedProperty.Value, loadedProperty.ValueOriginal));
                    }
                }

                if (data.data.TryGetValue(nameof(MaterialNamePropertyList), out var materialNameProperties) && materialNameProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialNameProperty>>((byte[])materialNameProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            MaterialNamePropertyList.Add(new MaterialNameProperty(loadedProperty.ObjectType, CurrentCoordinateIndex, loadedProperty.Slot, loadedProperty.Renderer, loadedProperty.MaterialName, loadedProperty.Value));
                    }
                }

                if (data.data.TryGetValue(nameof(MaterialFloatPropertyList), out var materialFloatProperties) && materialFloatProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialFloatProperty>>((byte[])materialFloatProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            MaterialFloatPropertyList.Add(new MaterialFloatProperty(loadedProperty.ObjectType, CurrentCoordinateIndex, loadedProperty.Slot, loadedProperty.MaterialName, loadedProperty.Property, loadedProperty.Value, loadedProperty.ValueOriginal));
                    }
                }

                if (data.data.TryGetValue(nameof(MaterialKeywordPropertyList), out var materialKeywordProperties) && materialKeywordProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialKeywordProperty>>((byte[])materialKeywordProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            MaterialKeywordPropertyList.Add(new MaterialKeywordProperty(loadedProperty.ObjectType, CurrentCoordinateIndex, loadedProperty.Slot, loadedProperty.MaterialName, loadedProperty.Property, loadedProperty.Value, loadedProperty.ValueOriginal));
                    }
                }

                if (data.data.TryGetValue(nameof(MaterialColorPropertyList), out var materialColorProperties) && materialColorProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialColorProperty>>((byte[])materialColorProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            MaterialColorPropertyList.Add(new MaterialColorProperty(loadedProperty.ObjectType, CurrentCoordinateIndex, loadedProperty.Slot, loadedProperty.MaterialName, loadedProperty.Property, loadedProperty.Value, loadedProperty.ValueOriginal));
                    }
                }

                if (data.data.TryGetValue(nameof(MaterialTexturePropertyList), out var materialTextureProperties) && materialTextureProperties != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialTextureProperty>>((byte[])materialTextureProperties);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                        {
                            int? texID = null;
                            if (loadedProperty.TexID != null)
                                texID = importDictionary[(int)loadedProperty.TexID];
                            MEAnimationUtil.RemapTexID(loadedProperty.TexAnimationDef, importDictionary);
                            MaterialTextureProperty newTextureProperty = new MaterialTextureProperty(loadedProperty.ObjectType, CurrentCoordinateIndex, loadedProperty.Slot, loadedProperty.MaterialName, loadedProperty.Property, texID, loadedProperty.Offset, loadedProperty.OffsetOriginal, loadedProperty.Scale, loadedProperty.ScaleOriginal, loadedProperty.TexAnimationDef);
                            MaterialTexturePropertyList.Add(newTextureProperty);
                        }
                    }
                }

                if (data.data.TryGetValue(nameof(MaterialCopyList), out var materialCopyData) && materialCopyData != null)
                {
                    var properties = MessagePackSerializer.Deserialize<List<MaterialCopy>>((byte[])materialCopyData);
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var loadedProperty = properties[i];
                        if (objectTypesToLoad.Contains(loadedProperty.ObjectType))
                            MaterialCopyList.Add(new MaterialCopy(loadedProperty.ObjectType, CurrentCoordinateIndex, loadedProperty.Slot, loadedProperty.MaterialName, loadedProperty.MaterialCopyName));
                    }
                }
            }
        }

        public IEnumerator LoadData(bool clothes, bool accessories, bool hair)
        {
            return LoadData(clothes, accessories, hair, true);
        }

        public IEnumerator LoadData(bool clothes, bool accessories, bool hair, bool body)
        {
            yield return null;
#if !EC
            if (KKAPI.Studio.StudioAPI.InsideStudio)
            {
                yield return null;
                yield return null;
            }
#endif
            while (ChaControl == null || ChaControl.GetHead() == null)
                yield return null;

            if (body)
                CorrectTongue();
#if KK || KKS
            if (KKAPI.Studio.StudioAPI.InsideStudio && body)
                CorrectFace();
#endif

            //Instantiate all material copies before applying any edits to ensure edits are applied to copies
            for (var i = 0; i < MaterialCopyList.Count; i++)
            {
                var property = MaterialCopyList[i];
                if (property.ObjectType == ObjectType.Clothing && !clothes) continue;
                if (property.ObjectType == ObjectType.Accessory && !accessories) continue;
                if (property.ObjectType == ObjectType.Hair && !hair) continue;
                if ((property.ObjectType == ObjectType.Clothing || property.ObjectType == ObjectType.Accessory) && property.CoordinateIndex != CurrentCoordinateIndex) continue;
                if (property.ObjectType == ObjectType.Character && !body) continue;

                CopyMaterial(FindGameObject(property.ObjectType, property.Slot), property.MaterialName, property.MaterialCopyName);
            }

            // Rename materials before applying edits, but after copying materials, to ensure no missing material mishaps occur
            // Do not move this anywhere else
            for (var i = 0; i < MaterialNamePropertyList.Count; i++)
            {
                var property = MaterialNamePropertyList[i];
                if (property.ObjectType == ObjectType.Clothing && !clothes) continue;
                if (property.ObjectType == ObjectType.Accessory && !accessories) continue;
                if (property.ObjectType == ObjectType.Hair && !hair) continue;
                if ((property.ObjectType == ObjectType.Clothing || property.ObjectType == ObjectType.Accessory) && property.CoordinateIndex != CurrentCoordinateIndex) continue;
                if (property.ObjectType == ObjectType.Character && !body) continue;

                MaterialAPI.SetName(FindGameObject(property.ObjectType, property.Slot), property.Renderer, property.MaterialName, property.Value);
            }

            for (var i = 0; i < MaterialShaderList.Count; i++)
            {
                var property = MaterialShaderList[i];
                if (property.ObjectType == ObjectType.Clothing && !clothes) continue;
                if (property.ObjectType == ObjectType.Accessory && !accessories) continue;
                if (property.ObjectType == ObjectType.Hair && !hair) continue;
                if ((property.ObjectType == ObjectType.Clothing || property.ObjectType == ObjectType.Accessory) && property.CoordinateIndex != CurrentCoordinateIndex) continue;
                if (property.ObjectType == ObjectType.Character && !body) continue;

#if KK || EC || KKS
                if (property.ObjectType == ObjectType.Character && MaterialEditorPlugin.EyeMaterials.Contains(property.MaterialName))
                {
                    SetShader(FindGameObject(property.ObjectType, property.Slot), property.MaterialName, property.ShaderName, true);
                }
                else
#endif
                {
                    SetShader(FindGameObject(property.ObjectType, property.Slot), property.MaterialName, property.ShaderName);
                }
                SetRenderQueue(FindGameObject(property.ObjectType, property.Slot), property.MaterialName, property.RenderQueue);
            }
            for (var i = 0; i < RendererPropertyList.Count; i++)
            {
                var property = RendererPropertyList[i];
#if KK
                if (property.Property == RendererProperties.UpdateWhenOffscreen) continue;
#endif
                if (property.ObjectType == ObjectType.Clothing && !clothes) continue;
                if (property.ObjectType == ObjectType.Accessory && !accessories) continue;
                if (property.ObjectType == ObjectType.Hair && !hair) continue;
                if ((property.ObjectType == ObjectType.Clothing || property.ObjectType == ObjectType.Accessory) && property.CoordinateIndex != CurrentCoordinateIndex) continue;
                if (property.ObjectType == ObjectType.Character && !body) continue;

                MaterialAPI.SetRendererProperty(FindGameObject(property.ObjectType, property.Slot), property.RendererName, property.Property, property.Value);
            }
            for (var i = 0; i < MaterialFloatPropertyList.Count; i++)
            {
                var property = MaterialFloatPropertyList[i];
                if (property.ObjectType == ObjectType.Clothing && !clothes) continue;
                if (property.ObjectType == ObjectType.Accessory && !accessories) continue;
                if (property.ObjectType == ObjectType.Hair && !hair) continue;
                if ((property.ObjectType == ObjectType.Clothing || property.ObjectType == ObjectType.Accessory) && property.CoordinateIndex != CurrentCoordinateIndex) continue;
                var go = FindGameObject(property.ObjectType, property.Slot);
                if (Instance.CheckBlacklist(property.MaterialName, property.Property)) continue;
                if (property.ObjectType == ObjectType.Character && !body) continue;

                SetFloat(go, property.MaterialName, property.Property, float.Parse(property.Value));
            }
            for (var i = 0; i < MaterialKeywordPropertyList.Count; i++)
            {
                var property = MaterialKeywordPropertyList[i];
                if (property.ObjectType == ObjectType.Clothing && !clothes) continue;
                if (property.ObjectType == ObjectType.Accessory && !accessories) continue;
                if (property.ObjectType == ObjectType.Hair && !hair) continue;
                if ((property.ObjectType == ObjectType.Clothing || property.ObjectType == ObjectType.Accessory) && property.CoordinateIndex != CurrentCoordinateIndex) continue;
                var go = FindGameObject(property.ObjectType, property.Slot);
                if (Instance.CheckBlacklist(property.MaterialName, property.Property)) continue;
                if (property.ObjectType == ObjectType.Character && !body) continue;

                SetKeyword(go, property.MaterialName, property.Property, property.Value);
            }
            for (var i = 0; i < MaterialColorPropertyList.Count; i++)
            {
                var property = MaterialColorPropertyList[i];
                if (property.ObjectType == ObjectType.Clothing && !clothes) continue;
                if (property.ObjectType == ObjectType.Accessory && !accessories) continue;
                if (property.ObjectType == ObjectType.Hair && !hair) continue;
                if ((property.ObjectType == ObjectType.Clothing || property.ObjectType == ObjectType.Accessory) && property.CoordinateIndex != CurrentCoordinateIndex) continue;
                var go = FindGameObject(property.ObjectType, property.Slot);
                if (Instance.CheckBlacklist(property.MaterialName, property.Property)) continue;
                if (property.ObjectType == ObjectType.Character && !body) continue;

                SetColor(go, property.MaterialName, property.Property, property.Value);
            }
            for (var i = 0; i < MaterialTexturePropertyList.Count; i++)
            {
                var property = MaterialTexturePropertyList[i];
                if (property.ObjectType == ObjectType.Clothing && !clothes) continue;
                if (property.ObjectType == ObjectType.Accessory && !accessories) continue;
                if (property.ObjectType == ObjectType.Hair && !hair) continue;
                if ((property.ObjectType == ObjectType.Clothing || property.ObjectType == ObjectType.Accessory) && property.CoordinateIndex != CurrentCoordinateIndex) continue;
                if (property.ObjectType == ObjectType.Character && !body) continue;
                var go = FindGameObject(property.ObjectType, property.Slot);
                if (Instance.CheckBlacklist(property.MaterialName, property.Property)) continue;

                SetTextureWithProperty(go, property);
                SetTextureOffset(go, property.MaterialName, property.Property, property.Offset);
                SetTextureScale(go, property.MaterialName, property.Property, property.Scale);
            }
            for (var i = 0; i < ProjectorPropertyList.Count; i++)
            {
                var property = ProjectorPropertyList[i];
                if (property.ObjectType == ObjectType.Clothing && !clothes) continue;
                if (property.ObjectType == ObjectType.Accessory && !accessories) continue;
                if (property.ObjectType == ObjectType.Hair && !hair) continue;
                if ((property.ObjectType == ObjectType.Clothing || property.ObjectType == ObjectType.Accessory) && property.CoordinateIndex != CurrentCoordinateIndex) continue;
                if (property.ObjectType == ObjectType.Character && !body) continue;

                MaterialAPI.SetProjectorProperty(FindGameObject(property.ObjectType, property.Slot), property.ProjectorName, property.Property, float.Parse(property.Value));
            }


#if KK || EC || KKS
            if (MaterialEditorPlugin.RimRemover.Value)
                RemoveRim();
#endif
        }
        /// <summary>
        /// Corrects the tongue materials since some of them are not properly refreshed on replacing a character
        /// </summary>
        private void CorrectTongue()
        {
#if KK || KKS
            if (!ChaControl.hiPoly) return;
#endif

#if KK || EC || KKS || AI || HS2
            //Get the tongue material used by the head since this one is properly refreshed with every character reload
            Material tongueMat = null;
            foreach (var renderer in GetRendererList(ChaControl.objHead.gameObject))
            {
                var mat = GetMaterials(ChaControl.gameObject, renderer).FirstOrDefault(x => x.name.Contains("tang"));
                if (mat != null)
                    tongueMat = mat;
            }

            //Set the materials of the other tongues to the one from the head
            if (tongueMat != null)
            {
                string shaderName = tongueMat.shader.NameFormatted();
                string materialName = tongueMat.NameFormatted();

                SetShader(ChaControl.gameObject, materialName, shaderName);

                foreach (var property in XMLShaderProperties[XMLShaderProperties.ContainsKey(shaderName) ? shaderName : "default"])
                {
                    if (property.Value.Type == ShaderPropertyType.Color)
                        SetColor(ChaControl.gameObject, materialName, property.Key, tongueMat.GetColor("_" + property.Key));
                    else if (property.Value.Type == ShaderPropertyType.Float)
                        SetFloat(ChaControl.gameObject, materialName, property.Key, tongueMat.GetFloat("_" + property.Key));
                    else if (property.Value.Type == ShaderPropertyType.Texture)
                        SetTexture(ChaControl.gameObject, materialName, property.Key, tongueMat.GetTexture("_" + property.Key));
                    else if (property.Value.Type == ShaderPropertyType.Keyword)
                        SetKeyword(ChaControl.gameObject, materialName, property.Key, tongueMat.IsKeywordEnabled("_" + property.Key));
                }
            }
#endif
        }

#if KK || KKS
        /// <summary>
        /// Force reload face textures
        /// </summary>
        private void CorrectFace()
        {
            ChaControl.ChangeSettingEyebrow();
            ChaControl.ChangeSettingEye(true, true, true);
            ChaControl.ChangeSettingEyeHiUp();
            ChaControl.ChangeSettingEyeHiDown();
            ChaControl.ChangeSettingEyelineUp();
            ChaControl.ChangeSettingEyelineDown();
            ChaControl.ChangeSettingWhiteOfEye(true, true);
            ChaControl.ChangeSettingNose();
        }
#endif

#if KK || EC || KKS
        private void RemoveRim()
        {
            for (var i = 0; i < ChaControl.objClothes.Length; i++)
                RemoveRimClothes(i);
            for (var i = 0; i < ChaControl.objHair.Length; i++)
                RemoveRimHair(i);
            for (var i = 0; i < ChaControl.GetAccessoryObjects().Length; i++)
                RemoveRimAccessory(i);
        }
        private void RemoveRimClothes(int slot)
        {
            var go = ChaControl.objClothes[slot];
            foreach (var renderer in GetRendererList(go))
                foreach (var material in GetMaterials(go, renderer))
                    if (material.HasProperty("_rimV") && GetMaterialFloatPropertyValue(slot, ObjectType.Clothing, material, "rimV", go) == null)
                        SetMaterialFloatProperty(slot, ObjectType.Clothing, material, "rimV", 0, go);
        }
        private IEnumerator RemoveRimHairCo(int slot)
        {
            yield return null;
            RemoveRimHair(slot);
        }
        private void RemoveRimHair(int slot)
        {
            var go = ChaControl.objHair[slot];
            foreach (var renderer in GetRendererList(go))
                foreach (var material in GetMaterials(go, renderer))
                    if (material.HasProperty("_rimV") && GetMaterialFloatPropertyValue(slot, ObjectType.Hair, material, "rimV", go) == null)
                        SetMaterialFloatProperty(slot, ObjectType.Hair, material, "rimV", 0, go);
        }
        private void RemoveRimAccessory(int slot)
        {
            var go = ChaControl.GetAccessoryObject(slot);
            if (go != null)
                foreach (var renderer in GetRendererList(go))
                    foreach (var material in GetMaterials(go, renderer))
                        if (material.HasProperty("_rimV") && GetMaterialFloatPropertyValue(slot, ObjectType.Accessory, material, "rimV", go) == null)
                            SetMaterialFloatProperty(slot, ObjectType.Accessory, material, "rimV", 0, go);
        }
#endif

        /// <summary>
        /// Finds the texture bytes in the dictionary of textures and returns its ID. If not found, adds the texture to the dictionary and returns the ID of the added texture.
        /// </summary>
        private int SetAndGetTextureID(byte[] textureBytes)
        {
            int highestID = 0;
            foreach (var tex in TextureDictionary)
                if (tex.Value.Data.SequenceEqualFast(textureBytes))
                    return tex.Key;
                else if (tex.Key > highestID)
                    highestID = tex.Key;

            highestID++;
            TextureDictionary.Add(highestID, new TextureContainer(textureBytes));
            return highestID;
        }

        internal void ClothesStateChangeEvent()
        {
            if (CoordinateChanging) return;
            if (MakerAPI.InsideMaker) return;

            ChaControl.StartCoroutine(LoadData(true, false, false));
        }

#if KK || KKS
        internal void CoordinateChangedEvent()
        {
            //In H if a coordinate is loaded the data will be overwritten. When switching coordinates the ExtSave data must be reloaded to restore the original.
            if (KKAPI.MainGame.GameAPI.InsideHScene)
                LoadCharacterExtSaveData();

            ChaControl.StartCoroutine(LoadData(true, true, false));

            if (MakerAPI.InsideAndLoaded)
                MaterialEditorUI.Visible = false;
        }

        internal void ClothingCopiedEvent(int copySource, int copyDestination, List<int> copySlots)
        {
            for (var i = 0; i < copySlots.Count; i++)
            {
                int slot = copySlots[i];
                MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copyDestination && x.Slot == slot);
                RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copyDestination && x.Slot == slot);
                MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copyDestination && x.Slot == slot);
                MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copyDestination && x.Slot == slot);
                MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copyDestination && x.Slot == slot);
                MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copyDestination && x.Slot == slot);
                MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copyDestination && x.Slot == slot);

                List<MaterialShader> newAccessoryMaterialShaderList = new List<MaterialShader>();
                List<RendererProperty> newAccessoryRendererPropertyList = new List<RendererProperty>();
                List<MaterialNameProperty> newAccessoryMaterialNamePropertyList = new List<MaterialNameProperty>();
                List<MaterialFloatProperty> newAccessoryMaterialFloatPropertyList = new List<MaterialFloatProperty>();
                List<MaterialColorProperty> newAccessoryMaterialColorPropertyList = new List<MaterialColorProperty>();
                List<MaterialTextureProperty> newAccessoryMaterialTexturePropertyList = new List<MaterialTextureProperty>();
                List<MaterialCopy> newMaterialCopyList = new List<MaterialCopy>();

                foreach (var property in MaterialShaderList.Where(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copySource && x.Slot == slot))
                    newAccessoryMaterialShaderList.Add(new MaterialShader(property.ObjectType, copyDestination, slot, property.MaterialName, property.ShaderName, property.ShaderNameOriginal, property.RenderQueue, property.RenderQueueOriginal));
                foreach (var property in RendererPropertyList.Where(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copySource && x.Slot == slot))
                    newAccessoryRendererPropertyList.Add(new RendererProperty(property.ObjectType, copyDestination, slot, property.RendererName, property.Property, property.Value, property.ValueOriginal));
                foreach (var property in MaterialNamePropertyList.Where(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copySource && x.Slot == slot))
                    newAccessoryMaterialNamePropertyList.Add(new MaterialNameProperty(property.ObjectType, copyDestination, slot, property.Renderer, property.MaterialName, property.Value));
                foreach (var property in MaterialFloatPropertyList.Where(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copySource && x.Slot == slot))
                    newAccessoryMaterialFloatPropertyList.Add(new MaterialFloatProperty(property.ObjectType, copyDestination, slot, property.MaterialName, property.Property, property.Value, property.ValueOriginal));
                foreach (var property in MaterialColorPropertyList.Where(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copySource && x.Slot == slot))
                    newAccessoryMaterialColorPropertyList.Add(new MaterialColorProperty(property.ObjectType, copyDestination, slot, property.MaterialName, property.Property, property.Value, property.ValueOriginal));
                foreach (var property in MaterialTexturePropertyList.Where(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copySource && x.Slot == slot))
                    newAccessoryMaterialTexturePropertyList.Add(new MaterialTextureProperty(property.ObjectType, copyDestination, slot, property.MaterialName, property.Property, property.TexID, property.Offset, property.OffsetOriginal, property.Scale, property.ScaleOriginal, property.TexAnimationDef));
                foreach (var property in MaterialCopyList.Where(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == copySource && x.Slot == slot))
                    newMaterialCopyList.Add(new MaterialCopy(property.ObjectType, copyDestination, slot, property.MaterialName, property.MaterialCopyName));

                MaterialShaderList.AddRange(newAccessoryMaterialShaderList);
                RendererPropertyList.AddRange(newAccessoryRendererPropertyList);
                MaterialNamePropertyList.AddRange(newAccessoryMaterialNamePropertyList);
                MaterialFloatPropertyList.AddRange(newAccessoryMaterialFloatPropertyList);
                MaterialColorPropertyList.AddRange(newAccessoryMaterialColorPropertyList);
                MaterialTexturePropertyList.AddRange(newAccessoryMaterialTexturePropertyList);
                MaterialCopyList.AddRange(newMaterialCopyList);

                if (copyDestination == CurrentCoordinateIndex)
                    MaterialEditorUI.Visible = false;

                ChaControl.StartCoroutine(LoadData(true, true, false));
            }

            PurgeUnusedAnimation();
        }
#endif

        internal void AccessoryKindChangeEvent(object sender, AccessorySlotEventArgs e)
        {
            if (AccessorySelectedSlotChanging) return;
            if (CoordinateChanging) return;

            //User switched accessories, remove all edited properties for this slot
            MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SlotIndex);
            RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SlotIndex);
            MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SlotIndex);
            MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SlotIndex);
            MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SlotIndex);
            MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SlotIndex);
            MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SlotIndex);

            if (MakerAPI.InsideAndLoaded)
                if (MaterialEditorUI.Visible && MEMaker.Instance != null)
                    MEMaker.Instance.UpdateUIAccessory();

#if KK || EC || KKS
            if (MaterialEditorPlugin.RimRemover.Value)
                RemoveRimAccessory(e.SlotIndex);
#endif

            PurgeUnusedAnimation();
        }

        internal void AccessorySelectedSlotChangeEvent(object sender, AccessorySlotEventArgs e)
        {
            if (!MakerAPI.InsideAndLoaded) return;

            AccessorySelectedSlotChanging = true;

#if KK || EC || KKS
            if (MakerAPI.InsideAndLoaded)
                if (MaterialEditorUI.Visible && MEMaker.Instance != null)
                    MEMaker.Instance.UpdateUIAccessory();
#else
            ChaControl.StartCoroutine(LoadData(false, true, false));
            ChaControl.StartCoroutine(RefreshUI());
            IEnumerator RefreshUI()
            {
                yield return null;
                if (MakerAPI.InsideAndLoaded)
                    if (MaterialEditorUI.Visible && MEMaker.Instance != null)
                        MEMaker.Instance.UpdateUIAccessory();
            }
#endif
        }

        internal void AccessoryTransferredEvent(object sender, AccessoryTransferEventArgs e)
        {
            MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.DestinationSlotIndex);
            RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.DestinationSlotIndex);
            MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.DestinationSlotIndex);
            MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.DestinationSlotIndex);
            MaterialKeywordPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.DestinationSlotIndex);
            MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.DestinationSlotIndex);
            MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.DestinationSlotIndex);
            MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.DestinationSlotIndex);

            List<MaterialShader> newAccessoryMaterialShaderList = new List<MaterialShader>();
            List<RendererProperty> newAccessoryRendererPropertyList = new List<RendererProperty>();
            List<MaterialNameProperty> newAccessoryMaterialNamePropertyList = new List<MaterialNameProperty>();
            List<MaterialFloatProperty> newAccessoryMaterialFloatPropertyList = new List<MaterialFloatProperty>();
            List<MaterialKeywordProperty> newAccessoryMaterialKeywordPropertyList = new List<MaterialKeywordProperty>();
            List<MaterialColorProperty> newAccessoryMaterialColorPropertyList = new List<MaterialColorProperty>();
            List<MaterialTextureProperty> newAccessoryMaterialTexturePropertyList = new List<MaterialTextureProperty>();
            List<MaterialCopy> newAccessoryMaterialCopyList = new List<MaterialCopy>();

            foreach (var property in MaterialShaderList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SourceSlotIndex))
                newAccessoryMaterialShaderList.Add(new MaterialShader(property.ObjectType, CurrentCoordinateIndex, e.DestinationSlotIndex, property.MaterialName, property.ShaderName, property.ShaderNameOriginal, property.RenderQueue, property.RenderQueueOriginal));
            foreach (var property in RendererPropertyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SourceSlotIndex))
                newAccessoryRendererPropertyList.Add(new RendererProperty(property.ObjectType, CurrentCoordinateIndex, e.DestinationSlotIndex, property.RendererName, property.Property, property.Value, property.ValueOriginal));
            foreach (var property in MaterialNamePropertyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SourceSlotIndex))
                newAccessoryMaterialNamePropertyList.Add(new MaterialNameProperty(property.ObjectType, CurrentCoordinateIndex, e.DestinationSlotIndex, property.Renderer, property.MaterialName, property.Value));
            foreach (var property in MaterialFloatPropertyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SourceSlotIndex))
                newAccessoryMaterialFloatPropertyList.Add(new MaterialFloatProperty(property.ObjectType, CurrentCoordinateIndex, e.DestinationSlotIndex, property.MaterialName, property.Property, property.Value, property.ValueOriginal));
            foreach (var property in MaterialKeywordPropertyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SourceSlotIndex))
                newAccessoryMaterialKeywordPropertyList.Add(new MaterialKeywordProperty(property.ObjectType, CurrentCoordinateIndex, e.DestinationSlotIndex, property.MaterialName, property.Property, property.Value, property.ValueOriginal));
            foreach (var property in MaterialColorPropertyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SourceSlotIndex))
                newAccessoryMaterialColorPropertyList.Add(new MaterialColorProperty(property.ObjectType, CurrentCoordinateIndex, e.DestinationSlotIndex, property.MaterialName, property.Property, property.Value, property.ValueOriginal));
            foreach (var property in MaterialTexturePropertyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SourceSlotIndex))
                newAccessoryMaterialTexturePropertyList.Add(new MaterialTextureProperty(property.ObjectType, CurrentCoordinateIndex, e.DestinationSlotIndex, property.MaterialName, property.Property, property.TexID, property.Offset, property.OffsetOriginal, property.Scale, property.ScaleOriginal, property.TexAnimationDef));
            foreach (var property in MaterialCopyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == e.SourceSlotIndex))
                newAccessoryMaterialCopyList.Add(new MaterialCopy(property.ObjectType, CurrentCoordinateIndex, e.DestinationSlotIndex, property.MaterialName, property.MaterialCopyName));

            MaterialShaderList.AddRange(newAccessoryMaterialShaderList);
            RendererPropertyList.AddRange(newAccessoryRendererPropertyList);
            MaterialNamePropertyList.AddRange(newAccessoryMaterialNamePropertyList);
            MaterialFloatPropertyList.AddRange(newAccessoryMaterialFloatPropertyList);
            MaterialKeywordPropertyList.AddRange(newAccessoryMaterialKeywordPropertyList);
            MaterialColorPropertyList.AddRange(newAccessoryMaterialColorPropertyList);
            MaterialTexturePropertyList.AddRange(newAccessoryMaterialTexturePropertyList);
            MaterialCopyList.AddRange(newAccessoryMaterialCopyList);

            if (MakerAPI.InsideAndLoaded)
                MaterialEditorUI.Visible = false;

            PurgeUnusedAnimation();

            ChaControl.StartCoroutine(LoadData(true, true, false));
        }

#if KK || KKS
        internal void AccessoriesCopiedEvent(object sender, AccessoryCopyEventArgs e)
        {
            foreach (int slot in e.CopiedSlotIndexes)
            {
                MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopyDestination && x.Slot == slot);
                RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopyDestination && x.Slot == slot);
                MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopyDestination && x.Slot == slot);
                MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopyDestination && x.Slot == slot);
                MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopyDestination && x.Slot == slot);
                MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopyDestination && x.Slot == slot);
                MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopyDestination && x.Slot == slot);

                List<MaterialShader> newAccessoryMaterialShaderList = new List<MaterialShader>();
                List<RendererProperty> newAccessoryRendererPropertyList = new List<RendererProperty>();
                List<MaterialNameProperty> newAccessoryMaterialNamePropertyList = new List<MaterialNameProperty>();
                List<MaterialFloatProperty> newAccessoryMaterialFloatPropertyList = new List<MaterialFloatProperty>();
                List<MaterialColorProperty> newAccessoryMaterialColorPropertyList = new List<MaterialColorProperty>();
                List<MaterialTextureProperty> newAccessoryMaterialTexturePropertyList = new List<MaterialTextureProperty>();
                List<MaterialCopy> newAccessoryMaterialCopyList = new List<MaterialCopy>();

                foreach (var property in MaterialShaderList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopySource && x.Slot == slot))
                    newAccessoryMaterialShaderList.Add(new MaterialShader(property.ObjectType, (int)e.CopyDestination, slot, property.MaterialName, property.ShaderName, property.ShaderNameOriginal, property.RenderQueue, property.RenderQueueOriginal));
                foreach (var property in RendererPropertyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopySource && x.Slot == slot))
                    newAccessoryRendererPropertyList.Add(new RendererProperty(property.ObjectType, (int)e.CopyDestination, slot, property.RendererName, property.Property, property.Value, property.ValueOriginal));
                foreach (var property in MaterialNamePropertyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopySource && x.Slot == slot))
                    newAccessoryMaterialNamePropertyList.Add(new MaterialNameProperty(property.ObjectType, (int)e.CopyDestination, slot, property.Renderer, property.MaterialName, property.Value));
                foreach (var property in MaterialFloatPropertyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopySource && x.Slot == slot))
                    newAccessoryMaterialFloatPropertyList.Add(new MaterialFloatProperty(property.ObjectType, (int)e.CopyDestination, slot, property.MaterialName, property.Property, property.Value, property.ValueOriginal));
                foreach (var property in MaterialColorPropertyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopySource && x.Slot == slot))
                    newAccessoryMaterialColorPropertyList.Add(new MaterialColorProperty(property.ObjectType, (int)e.CopyDestination, slot, property.MaterialName, property.Property, property.Value, property.ValueOriginal));
                foreach (var property in MaterialTexturePropertyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopySource && x.Slot == slot))
                    newAccessoryMaterialTexturePropertyList.Add(new MaterialTextureProperty(property.ObjectType, (int)e.CopyDestination, slot, property.MaterialName, property.Property, property.TexID, property.Offset, property.OffsetOriginal, property.Scale, property.ScaleOriginal, property.TexAnimationDef));
                foreach (var property in MaterialCopyList.Where(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == (int)e.CopySource && x.Slot == slot))
                    newAccessoryMaterialCopyList.Add(new MaterialCopy(property.ObjectType, (int)e.CopyDestination, slot, property.MaterialName, property.MaterialCopyName));

                MaterialShaderList.AddRange(newAccessoryMaterialShaderList);
                RendererPropertyList.AddRange(newAccessoryRendererPropertyList);
                MaterialNamePropertyList.AddRange(newAccessoryMaterialNamePropertyList);
                MaterialFloatPropertyList.AddRange(newAccessoryMaterialFloatPropertyList);
                MaterialColorPropertyList.AddRange(newAccessoryMaterialColorPropertyList);
                MaterialTexturePropertyList.AddRange(newAccessoryMaterialTexturePropertyList);
                MaterialCopyList.AddRange(newAccessoryMaterialCopyList);

                if (MakerAPI.InsideAndLoaded)
                    if ((int)e.CopyDestination == CurrentCoordinateIndex)
                        MaterialEditorUI.Visible = false;
            }

            PurgeUnusedAnimation();
        }
#endif

        internal void ChangeAccessoryEvent(int slot, int type)
        {
            if (MEMaker.Instance != null)
                MEMaker.ToggleButtonVisibility();

#if AI || HS2
            if (type != 350) return; //type 350 = no category, accessory being removed
#elif KK || EC || KKS
            if (type != 120) //type 120 = no category, accessory being removed
            {
                if (MaterialEditorPlugin.RimRemover.Value)
                    RemoveRimAccessory(slot);
                return;
            }
#endif
            if (!MakerAPI.InsideAndLoaded) return;
            if (CoordinateChanging) return;

            MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            MaterialKeywordPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Accessory && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);

            if (MakerAPI.InsideAndLoaded)
                MaterialEditorUI.Visible = false;

            PurgeUnusedAnimation();
        }

        internal void ChangeCustomClothesEvent(int slot)
        {
            if (!MakerAPI.InsideAndLoaded) return;
            if (CoordinateChanging) return;
            if (ClothesChanging) return;
            if (CharacterLoading) return;
            if (RefreshingTextures) return;
            if (CustomClothesOverride) return;
            if (new System.Diagnostics.StackTrace().ToString().Contains("KoiClothesOverlayController"))
            {
                StartCoroutine(LoadData(true, false, false, false));
                RefreshingTextures = true;
                return;
            }

            ClothesChanging = true;

            MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            MaterialKeywordPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);
            MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Clothing && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot);

            if (MakerAPI.InsideAndLoaded)
                MaterialEditorUI.Visible = false;

#if KK || EC || KKS
            if (MaterialEditorPlugin.RimRemover.Value)
                RemoveRimClothes(slot);
#elif PH
            //Reapply edits for other clothes since they will have been undone
            ChaControl.StartCoroutine(LoadData(true, true, false));
#endif

            PurgeUnusedAnimation();
        }

        internal void ChangeHairEvent(int slot)
        {
            if (!MakerAPI.InsideAndLoaded) return;
            if (CharacterLoading) return;

            MaterialShaderList.RemoveAll(x => x.ObjectType == ObjectType.Hair && x.Slot == slot);
            RendererPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair && x.Slot == slot);
            MaterialNamePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair && x.Slot == slot);
            MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair && x.Slot == slot);
            MaterialKeywordPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair && x.Slot == slot);
            MaterialColorPropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair && x.Slot == slot);
            MaterialTexturePropertyList.RemoveAll(x => x.ObjectType == ObjectType.Hair && x.Slot == slot);
            MaterialCopyList.RemoveAll(x => x.ObjectType == ObjectType.Hair && x.Slot == slot);

            if (MakerAPI.InsideAndLoaded)
                MaterialEditorUI.Visible = false;

#if KK || EC || KKS
            if (MaterialEditorPlugin.RimRemover.Value)
                StartCoroutine(RemoveRimHairCo(slot));
#elif PH
            //Reapply edits for other hairs since they will have been undone
            ChaControl.StartCoroutine(LoadData(false, false, true));
#endif

            PurgeUnusedAnimation();
        }

        internal void HandleMaterialNameChange(int slot, ObjectType objectType, Renderer renderer, Material material, string value, GameObject go)
        {
            value = value.FormatShadingObjectName();

            // Check for an existing material on the renderer by the same name
            // Also check if we're renaming a copied material, and find the actual material being renamed
            Material existing = null;
            Material copiedOriginalMat = null;
            foreach (var rend in GetRendererList(go))
            {
                foreach (var mat in GetMaterials(go, rend))
                {
                    if (mat.NameFormatted() == value)
                    {
                        if (rend == renderer) return;
                        existing = mat;
                    }
                    else if (material.name.Contains(MaterialCopyPostfix) && rend == renderer && mat.NameFormatted() == material.NameFormatted())
                    {
                        copiedOriginalMat = mat;
                    }
                }
            }

            if (existing == null)
            {
                int idx = GetCoordinateIndex(objectType);
                var shader = MaterialShaderList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == idx && x.Slot == slot && x.MaterialName == material.NameFormatted()).ToList();
                var textures = MaterialTexturePropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == idx && x.Slot == slot && x.MaterialName == material.NameFormatted()).ToList();
                var colors = MaterialColorPropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == idx && x.Slot == slot && x.MaterialName == material.NameFormatted()).ToList();
                var floats = MaterialFloatPropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == idx && x.Slot == slot && x.MaterialName == material.NameFormatted()).ToList();
                var keywords = MaterialKeywordPropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == idx && x.Slot == slot && x.MaterialName == material.NameFormatted()).ToList();
                if (shader.Count == 1) MaterialShaderList.Add(new MaterialShader(objectType, idx, slot, value, shader[0].ShaderName, shader[0].ShaderNameOriginal, shader[0].RenderQueue, shader[0].RenderQueueOriginal));
                foreach (var tex in textures) MaterialTexturePropertyList.Add(new MaterialTextureProperty(objectType, idx, slot, value, tex.Property, tex.TexID, tex.Offset, tex.OffsetOriginal, tex.Scale, tex.ScaleOriginal, tex.TexAnimationDef));
                foreach (var col in colors) MaterialColorPropertyList.Add(new MaterialColorProperty(objectType, idx, slot, value, col.Property, col.Value, col.ValueOriginal));
                foreach (var _float in floats) MaterialFloatPropertyList.Add(new MaterialFloatProperty(objectType, idx, slot, value, _float.Property, _float.Value, _float.ValueOriginal));
                foreach (var kw in keywords) MaterialKeywordPropertyList.Add(new MaterialKeywordProperty(objectType, idx, slot, value, kw.Property, kw.Value, kw.ValueOriginal));
            }
            else if (!material.name.Contains(MaterialCopyPostfix))
            {
                material.shader = existing.shader;
                material.shaderKeywords = existing.shaderKeywords;
                material.color = existing.color;
                material.mainTexture = existing.mainTexture;
                material.mainTextureOffset = existing.mainTextureOffset;
                material.mainTextureScale = existing.mainTextureScale;
                material.renderQueue = existing.renderQueue;
            }
            else if (copiedOriginalMat != null)
            {
                copiedOriginalMat.shader = existing.shader;
                copiedOriginalMat.shaderKeywords = existing.shaderKeywords;
                copiedOriginalMat.color = existing.color;
                copiedOriginalMat.mainTexture = existing.mainTexture;
                copiedOriginalMat.mainTextureOffset = existing.mainTextureOffset;
                copiedOriginalMat.mainTextureScale = existing.mainTextureScale;
                copiedOriginalMat.renderQueue = existing.renderQueue;
            }
        }

        /// <summary>
        /// Refresh the clothes MainTex, typically called after editing colors in the character maker
        /// </summary>
        public void RefreshClothesMainTex() => StartCoroutine(RefreshClothesMainTexCoroutine());
        private IEnumerator RefreshClothesMainTexCoroutine()
        {
            yield return new WaitForEndOfFrame();
            for (var i = 0; i < MaterialTexturePropertyList.Count; i++)
            {
                var property = MaterialTexturePropertyList[i];
                if (Instance.CheckBlacklist(property.MaterialName, property.Property))
                    continue;

                if (property.ObjectType != ObjectType.Clothing || property.CoordinateIndex != CurrentCoordinateIndex || property.Property != "MainTex")
                    continue;

                if (property.TexID != null)
                {
                    var tex = TextureDictionary[(int)property.TexID].Texture;
                    MaterialEditorPlugin.Instance.ConvertNormalMap(ref tex, property.Property);
                    SetTexture(FindGameObject(ObjectType.Clothing, property.Slot), property.MaterialName, property.Property, tex);
                }
            }
        }

        /// <summary>
        /// Sets the texture indicated by TexID to texture of Material indicated by TextureProperty
        /// </summary>
        /// <param name="go">GameObject to search for the renderer</param>
        /// <param name="textureProperty">TextureProperty with TexID to set for Material</param>
        /// <returns>True if the value was set, false if it could not be set</returns>
        private bool SetTextureWithProperty(GameObject go, MaterialTextureProperty textureProperty)
        {
            if (!textureProperty.TexID.HasValue || textureProperty.NullCheck())
                return false;

            int texID = textureProperty.TexID.Value;
            if (!TextureDictionary.TryGetValue(texID, out var container))
                return false;

            if (textureProperty.TexAnimationDef == null)
            {
                //Does not have animation

                AnimationControllerMap.Remove(textureProperty); //If have animation, delete it.

                var tex = container.Texture;
                MaterialEditorPlugin.Instance.ConvertNormalMap(ref tex, textureProperty.Property);
                return SetTexture(go, textureProperty.MaterialName, textureProperty.Property, tex);
            }
            else
            {
                if (AnimationControllerMap.TryGetValue(textureProperty, out var controller))
                {
                    controller.go = go;
                    if (textureProperty.TexAnimationDef != controller.def)
                        controller.Reset(textureProperty.TexAnimationDef);
                }
                else
                {
                    controller = new MEAnimationController(this, go, textureProperty.TexAnimationDef);
                    AnimationControllerMap[textureProperty] = controller;
                }

                controller.UpdateAnimation(textureProperty);
                return true;
            }
        }

        /// <summary>
        /// Refresh the body MainTex, typically called after editing colors in the character maker
        /// </summary>
        public void RefreshBodyMainTex() => StartCoroutine(RefreshBodyMainTexCoroutine());
        private IEnumerator RefreshBodyMainTexCoroutine()
        {
            yield return new WaitForEndOfFrame();

            for (var i = 0; i < MaterialTexturePropertyList.Count; i++)
            {
                var property = MaterialTexturePropertyList[i];
                if (Instance.CheckBlacklist(property.MaterialName, property.Property))
                    continue;

                if (property.ObjectType == ObjectType.Character && property.Property == "MainTex")
                    SetTextureWithProperty(ChaControl.gameObject, property);
            }
        }
        /// <summary>
        /// Reapply all edits to the body and face
        /// </summary>
        public void RefreshBodyEdits()
        {
            if (CharacterLoading) return;
            StartCoroutine(LoadData(false, false, false));
        }
        /// <summary>
        /// Copy any edits for the specified object
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="go">GameObject the material belongs to</param>
        public void MaterialCopyEdits(int slot, ObjectType objectType, Material material, GameObject go)
        {
            CopyData.ClearAll();

            foreach (var materialShader in MaterialShaderList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted()))
                CopyData.MaterialShaderList.Add(new CopyContainer.MaterialShader(materialShader.ShaderName, materialShader.RenderQueue));
            foreach (var materialFloatProperty in MaterialFloatPropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted()))
                CopyData.MaterialFloatPropertyList.Add(new CopyContainer.MaterialFloatProperty(materialFloatProperty.Property, float.Parse(materialFloatProperty.Value)));
            foreach (var materialKeywordProperty in MaterialKeywordPropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted()))
                CopyData.MaterialKeywordPropertyList.Add(new CopyContainer.MaterialKeywordProperty(materialKeywordProperty.Property, materialKeywordProperty.Value));
            foreach (var materialColorProperty in MaterialColorPropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted()))
                CopyData.MaterialColorPropertyList.Add(new CopyContainer.MaterialColorProperty(materialColorProperty.Property, materialColorProperty.Value));
            foreach (var materialTextureProperty in MaterialTexturePropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted()))
            {
                if (materialTextureProperty.TexID != null)
                    CopyData.MaterialTexturePropertyList.Add(new CopyContainer.MaterialTextureProperty(materialTextureProperty.Property, TextureDictionary[(int)materialTextureProperty.TexID].Data, materialTextureProperty.Offset, materialTextureProperty.Scale));
                else
                    CopyData.MaterialTexturePropertyList.Add(new CopyContainer.MaterialTextureProperty(materialTextureProperty.Property, null, materialTextureProperty.Offset, materialTextureProperty.Scale));
            }
            if (GetProjectorList(objectType, go).FirstOrDefault(x => x.material == material) != null)
                foreach (var projectorProperty in ProjectorPropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.ProjectorName == material.NameFormatted()))
                    CopyData.ProjectorPropertyList.Add(new CopyContainer.ProjectorProperty(projectorProperty.Property, float.Parse(projectorProperty.Value)));
            
        }
        /// <summary>
        /// Paste any edits for the specified object
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void MaterialPasteEdits(int slot, ObjectType objectType, Material material, GameObject go, bool setProperty = true)
        {
            for (var i = 0; i < CopyData.MaterialShaderList.Count; i++)
            {
                var materialShader = CopyData.MaterialShaderList[i];
                if (materialShader.ShaderName != null)
                    SetMaterialShader(slot, objectType, material, materialShader.ShaderName, go, setProperty);
                if (materialShader.RenderQueue != null)
                    SetMaterialShaderRenderQueue(slot, objectType, material, (int)materialShader.RenderQueue, go, setProperty);
            }
            for (var i = 0; i < CopyData.MaterialFloatPropertyList.Count; i++)
            {
                var materialFloatProperty = CopyData.MaterialFloatPropertyList[i];
                if (material.HasProperty($"_{materialFloatProperty.Property}"))
                    SetMaterialFloatProperty(slot, objectType, material, materialFloatProperty.Property, materialFloatProperty.Value, go, setProperty);
            }
            for (var i = 0; i < CopyData.MaterialKeywordPropertyList.Count; i++)
            {
                var materialKeywordProperty = CopyData.MaterialKeywordPropertyList[i];
                SetMaterialKeywordProperty(slot, objectType, material, materialKeywordProperty.Property, materialKeywordProperty.Value, go, setProperty);
            }
            for (var i = 0; i < CopyData.MaterialColorPropertyList.Count; i++)
            {
                var materialColorProperty = CopyData.MaterialColorPropertyList[i];
                if (material.HasProperty($"_{materialColorProperty.Property}"))
                    SetMaterialColorProperty(slot, objectType, material, materialColorProperty.Property, materialColorProperty.Value, go, setProperty);
            }
            for (var i = 0; i < CopyData.MaterialTexturePropertyList.Count; i++)
            {
                var materialTextureProperty = CopyData.MaterialTexturePropertyList[i];
                if (material.HasProperty($"_{materialTextureProperty.Property}"))
                    SetMaterialTexture(slot, objectType, material, materialTextureProperty.Property, materialTextureProperty.Data, go);
                if (materialTextureProperty.Offset != null)
                    SetMaterialTextureOffset(slot, objectType, material, materialTextureProperty.Property, (Vector2)materialTextureProperty.Offset, go, setProperty);
                if (materialTextureProperty.Scale != null)
                    SetMaterialTextureScale(slot, objectType, material, materialTextureProperty.Property, (Vector2)materialTextureProperty.Scale, go, setProperty);
            }

            var projector = GetProjectorList(objectType, go).FirstOrDefault(x => x.material == material);
            if (projector != null)
                for (var i = 0; i < CopyData.MaterialTexturePropertyList.Count; i++)
                {
                    var projectorProperty = CopyData.ProjectorPropertyList[i];
                    SetProjectorProperty(slot, objectType, projector, projectorProperty.Property, projectorProperty.Value, go, setProperty);
                }
        }

        /// <summary> miuna
        /// 
        public void MaterialOutputEdits(int slot, ObjectType objectType, Material _material, GameObject go, bool setProperty = true)
        {
            Debug.LogWarning(slot);
            Debug.LogWarning(_material.name);
            SkinnedMeshRenderer[] skinnedMeshRenderers = GameObject.Find("BodyTop").transform.
                GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);

            if (skinnedMeshRenderers == null || skinnedMeshRenderers.Length == 0)
                throw new Exception("No SkinnedMeshRenderers found in the scene.");
            HashSet<Material> materials = new HashSet<Material>();
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                if (skinnedMeshRenderer == null || skinnedMeshRenderer.sharedMaterials == null)
                    continue;

                foreach (var material in skinnedMeshRenderer.sharedMaterials)
                {
                    if (material != null && !materials.Contains(material))
                    {
                        materials.Add(material);
                        Debug.LogWarning($"GetObjectMaterials: {material.name}");
                        //MaterialExportEdits(slot, objectType, material, go);
                        ExportMaterial(material);
                    }
                }
            }

           // ExportAllMaterials();
        }


        public void ExportMaterial(Material material)
        {
            string path = Path.Combine(ExportPath, "Material");
            // 确保存储文件的目录存在，若不存在则创建
            Directory.CreateDirectory(path);
            path = Path.Combine(path, $"{material.NameFormatted()}");
            Directory.CreateDirectory(path);
            var shaderData = new Dictionary<string, object>();
            Debug.LogWarning($"MaterialExportEdits: Exporting material {material.NameFormatted()} to {path}");
            foreach (var materialShader in MaterialShaderList.Where(x => x.MaterialName == material.NameFormatted()))
            {
                if (shaderData.ContainsKey("ShaderName"))
                {
                    Debug.LogWarning($"MaterialExportEdits: {materialShader.ShaderName} already exists in shaderData, skipping.");
                    continue;
                }
                shaderData.Add("ShaderName", materialShader.ShaderName);
                shaderData.Add("RenderQueue", materialShader.RenderQueue);
            }

            foreach (var materialFloatProperty in MaterialFloatPropertyList.Where(x => x.MaterialName == material.NameFormatted()))
            {
                if (shaderData.ContainsKey(materialFloatProperty.Property))
                {
                    Debug.LogWarning($"MaterialExportEdits: {materialFloatProperty.Property} already exists in shaderData, skipping.");
                    continue;
                }
                shaderData.Add(materialFloatProperty.Property, materialFloatProperty.Value);
            }

            foreach (var materialKeywordProperty in MaterialKeywordPropertyList.Where(x => x.MaterialName == material.NameFormatted()))
            {
                if (shaderData.ContainsKey(materialKeywordProperty.Property))
                {
                    Debug.LogWarning($"MaterialExportEdits: {materialKeywordProperty.Property} already exists in shaderData, skipping.");
                    continue;
                }
                shaderData.Add(materialKeywordProperty.Property, materialKeywordProperty.Value);
            }
            foreach (var materialColorProperty in MaterialColorPropertyList.Where(x => x.MaterialName == material.NameFormatted()))
            {
                if (shaderData.ContainsKey(materialColorProperty.Property))
                {
                    Debug.LogWarning($"MaterialExportEdits: {materialColorProperty.Property} already exists in shaderData, skipping.");
                    continue;
                }
                shaderData.Add(materialColorProperty.Property, materialColorProperty.Value);
            }
            foreach (var materialTextureProperty in MaterialTexturePropertyList.Where(x => x.MaterialName == material.NameFormatted()))
            {

                if (materialTextureProperty.Offset != null)
                {
                    if (shaderData.ContainsKey($"{materialTextureProperty.Property}_Offset"))
                    {
                        Debug.LogWarning($"MaterialExportEdits: {materialTextureProperty.Property}_Offset already exists in shaderData, skipping.");
                        continue;
                    }
                    shaderData.Add($"{materialTextureProperty.Property}_Offset", materialTextureProperty.Offset);
                }
                if (materialTextureProperty.Scale != null)
                {
                    if (shaderData.ContainsKey($"{materialTextureProperty.Property}_Scale"))
                    {
                        Debug.LogWarning($"MaterialExportEdits: {materialTextureProperty.Property}_Scale already exists in shaderData, skipping.");
                        continue;
                    }
                    shaderData.Add($"{materialTextureProperty.Property}_Scale", materialTextureProperty.Scale);
                }
                ExportTexture(material, materialTextureProperty.Property, path);
            }
               // 转换为 JSON
            string json = GenerateJson(shaderData);
            path = Path.Combine(path, $"{material.NameFormatted()}_{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.json");

            // 写入文件
            File.WriteAllText(path, json);
        }
        // public void MaterialExportEdits(int slot, ObjectType objectType, Material material, GameObject go, bool setProperty = true)
        // {

        //     MaterialCopyEdits(slot, objectType, material, go);
        //     var shaderData = new Dictionary<string, object>();

        //     string path = Path.Combine(ExportPath, "Material");
        //     // 确保存储文件的目录存在，若不存在则创建
        //     Directory.CreateDirectory(Path.GetDirectoryName(path));
        //     path = Path.Combine(path, $"{shaderData["ShaderName"]}");
        //     Directory.CreateDirectory(Path.GetDirectoryName(path));
        //     Debug.LogWarning($"击中shader数量为 : {CopyData.MaterialShaderList.Count}");
        //     for (var i = 0; i < CopyData.MaterialShaderList.Count; i++)
        //     {
        //         var materialShader = CopyData.MaterialShaderList[i];
        //         if (materialShader.ShaderName != null)
        //             shaderData.Add("ShaderName", materialShader.ShaderName);
        //         Debug.LogWarning($"ShaderName: {materialShader.ShaderName}");
        //         if (materialShader.RenderQueue != null)
        //             shaderData.Add("RenderQueue", materialShader.RenderQueue);
        //         Debug.LogWarning($"RenderQueue: {materialShader.RenderQueue}");
        //     }
        //     for (var i = 0; i < CopyData.MaterialFloatPropertyList.Count; i++)
        //     {
        //         var materialFloatProperty = CopyData.MaterialFloatPropertyList[i];
        //         if (material.HasProperty($"_{materialFloatProperty.Property}"))
        //             shaderData.Add(materialFloatProperty.Property, materialFloatProperty.Value);
        //         Debug.LogWarning($"materialFloatProperty: {materialFloatProperty.Property}:{materialFloatProperty.Value}");
        //     }
        //     for (var i = 0; i < CopyData.MaterialKeywordPropertyList.Count; i++)
        //     {
        //         var materialKeywordProperty = CopyData.MaterialKeywordPropertyList[i];
        //         shaderData.Add(materialKeywordProperty.Property, materialKeywordProperty.Value);
        //         Debug.LogWarning($"materialKeywordProperty: {materialKeywordProperty.Property}:{materialKeywordProperty.Value}");
        //     }
        //     for (var i = 0; i < CopyData.MaterialColorPropertyList.Count; i++)
        //     {
        //         var materialColorProperty = CopyData.MaterialColorPropertyList[i];
        //         shaderData.Add(materialColorProperty.Property, materialColorProperty.Value);
        //         Debug.LogWarning($"materialColorProperty: {materialColorProperty.Property}:{materialColorProperty.Value}");
        //     }
        //     for (var i = 0; i < CopyData.MaterialTexturePropertyList.Count; i++)
        //     {
        //         var materialTextureProperty = CopyData.MaterialTexturePropertyList[i];
        //         if (material.HasProperty($"_{materialTextureProperty.Property}"))
        //         {
        //             //var tex = material.GetTexture($"_{materialTextureProperty.Property}");
        //             ExportTexture(material, materialTextureProperty.Property, path);
        //         }
        //         Debug.LogWarning($"materialTextureProperty: {materialTextureProperty.Property}:{materialTextureProperty.Data}");
        //         if (materialTextureProperty.Offset != null)
        //         {
        //             shaderData.Add($"{materialTextureProperty.Property}_Offset", materialTextureProperty.Offset);
        //             Debug.LogWarning($"materialTextureProperty Offset: {materialTextureProperty.Offset}");
        //         }
        //         if (materialTextureProperty.Scale != null)
        //         {
        //             shaderData.Add($"{materialTextureProperty.Property}_Scale", materialTextureProperty.Scale);
        //             Debug.LogWarning($"materialTextureProperty Scale: {materialTextureProperty.Scale}");
        //         }
        //     }
        //     // 转换为 JSON
        //     string json = GenerateJson(shaderData);
        //     string prefix = shaderData.ContainsKey("ShaderName") ? shaderData["ShaderName"].ToString() : "MaterialExport";
        //     path = Path.Combine(path, $"{prefix}_{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.json");
        //     // 写入文件
        //     File.WriteAllText(path, json);
        // }

        private string GenerateJson(Dictionary<string, object> shaderData)
        {
            
            // 手动拼接 JSON 字符串
            string json = "{\n";
            int count = shaderData.Count;
            int index = 0;
            foreach (var pair in shaderData)
            {
                json += $"  \"{pair.Key}\": {pair.Value}";
                if (index < count - 1)
                    json += ",\n";
                else
                    json += "\n";
                index++;
            }
            json += "}";

            return json;
        }

        //because UI is private
        private static void ExportTexture(Material mat, string property,string path)
        {
            var tex = mat.GetTexture($"_{property}");
            if (tex == null) return;
            var matName = mat.NameFormatted();
            matName = string.Concat(matName.Split(Path.GetInvalidFileNameChars())).Trim();
            string filename = Path.Combine(path, $"{matName}_{property}_{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.png");
            Instance.ConvertNormalMap(ref tex, property, ConvertNormalmapsOnExport.Value);
            SaveTex(tex, filename);
            MaterialEditorPluginBase.Logger.LogInfo($"Exported {filename}");
            //Utilities.OpenFileInExplorer(filename);
        }

        private static List<Material> ExportAllMaterials()
        {
            SkinnedMeshRenderer[] skinnedMeshRenderers = GameObject.Find("BodyTop").transform.
                GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);

            if (skinnedMeshRenderers == null || skinnedMeshRenderers.Length == 0)
                throw new Exception("No SkinnedMeshRenderers found in the scene.");
            HashSet<Material> materials = new HashSet<Material>();
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                if (skinnedMeshRenderer == null || skinnedMeshRenderer.sharedMaterials == null)
                    continue;

                foreach (var material in skinnedMeshRenderer.sharedMaterials)
                {
                    if (material != null && !materials.Contains(material))
                    {
                        materials.Add(material);
                        Debug.LogWarning($"GetObjectMaterials: {material.name}");
                    }
                }
            }
            return materials.ToList();
            // foreach (var renderer in GetRendererList(gameObject))
            // {
            //     Debug.LogWarning($"GetRenders: {renderer.name}");
            //     foreach (var material in GetMaterials(gameObject, renderer))
            //     {
            //         materials.Add(material);
            //         Debug.LogWarning($"GetObjectMaterials: {material.name}");
            //     }
            // }
            // return materials.ToList();
        }

        //end



        public void MaterialCopyRemove(int slot, ObjectType objectType, Material material, GameObject go)
        {
            string matName = material.NameFormatted();
            if (matName.Contains(MaterialCopyPostfix))
            {
                MaterialNamePropertyList.RemoveAll(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType == objectType && x.Slot == slot && x.Value == material.name);

                RemoveMaterial(go, material);
                MaterialShaderList.RemoveAll(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType == objectType && x.Slot == slot && x.MaterialName == matName);
                MaterialFloatPropertyList.RemoveAll(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType == objectType && x.Slot == slot && x.MaterialName == matName);
                MaterialKeywordPropertyList.RemoveAll(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType == objectType && x.Slot == slot && x.MaterialName == matName);
                MaterialColorPropertyList.RemoveAll(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType == objectType && x.Slot == slot && x.MaterialName == matName);
                MaterialTexturePropertyList.RemoveAll(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType == objectType && x.Slot == slot && x.MaterialName == matName);
                MaterialCopyList.RemoveAll(x => x.CoordinateIndex == CurrentCoordinateIndex && x.ObjectType == objectType && x.Slot == slot && x.MaterialCopyName == matName);
            }
            else if (GetMaterialNamePropertyValue(slot, objectType, GetRendererList(go).FirstOrDefault(x => x.materials.Contains(material)), material, go) == string.Empty)
            {
                string newMatName = CopyMaterial(go, matName);
                MaterialCopyList.Add(new MaterialCopy(objectType, CurrentCoordinateIndex, slot, matName, newMatName));

                List<MaterialShader> newAccessoryMaterialShaderList = new List<MaterialShader>();
                List<MaterialFloatProperty> newAccessoryMaterialFloatPropertyList = new List<MaterialFloatProperty>();
                List<MaterialKeywordProperty> newAccessoryMaterialKeywordPropertyList = new List<MaterialKeywordProperty>();
                List<MaterialColorProperty> newAccessoryMaterialColorPropertyList = new List<MaterialColorProperty>();
                List<MaterialTextureProperty> newAccessoryMaterialTexturePropertyList = new List<MaterialTextureProperty>();

                foreach (var property in MaterialShaderList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot && x.MaterialName == matName))
                    newAccessoryMaterialShaderList.Add(new MaterialShader(property.ObjectType, property.CoordinateIndex, slot, newMatName, property.ShaderName, property.ShaderNameOriginal, property.RenderQueue, property.RenderQueueOriginal));
                foreach (var property in MaterialFloatPropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot && x.MaterialName == matName))
                    newAccessoryMaterialFloatPropertyList.Add(new MaterialFloatProperty(property.ObjectType, property.CoordinateIndex, slot, newMatName, property.Property, property.Value, property.ValueOriginal));
                foreach (var property in MaterialKeywordPropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot && x.MaterialName == matName))
                    newAccessoryMaterialKeywordPropertyList.Add(new MaterialKeywordProperty(property.ObjectType, property.CoordinateIndex, slot, newMatName, property.Property, property.Value, property.ValueOriginal));
                foreach (var property in MaterialColorPropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot && x.MaterialName == matName))
                    newAccessoryMaterialColorPropertyList.Add(new MaterialColorProperty(property.ObjectType, property.CoordinateIndex, slot, newMatName, property.Property, property.Value, property.ValueOriginal));
                foreach (var property in MaterialTexturePropertyList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == CurrentCoordinateIndex && x.Slot == slot && x.MaterialName == matName))
                    newAccessoryMaterialTexturePropertyList.Add(new MaterialTextureProperty(property.ObjectType, property.CoordinateIndex, slot, newMatName, property.Property, property.TexID, property.Offset, property.OffsetOriginal, property.Scale, property.ScaleOriginal, property.TexAnimationDef));

                MaterialShaderList.AddRange(newAccessoryMaterialShaderList);
                MaterialFloatPropertyList.AddRange(newAccessoryMaterialFloatPropertyList);
                MaterialKeywordPropertyList.AddRange(newAccessoryMaterialKeywordPropertyList);
                MaterialColorPropertyList.AddRange(newAccessoryMaterialColorPropertyList);
                MaterialTexturePropertyList.AddRange(newAccessoryMaterialTexturePropertyList);
            }
            else
            {
                MaterialEditorPlugin.Logger.LogMessage("Cannot copy renamed materials!");
            }

            PurgeUnusedAnimation();
        }

        #region Set, Get, Remove methods
        /// <summary>
        /// Get the original value of the saved projector property value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="projector">Projector being modified</param>
        /// <param name="property">Property of the projector</param>
        /// <param name="go">GameObject the projector belongs to</param>
        /// <returns>Saved projector property value</returns>
        public float? GetProjectorPropertyValueOriginal(int slot, ObjectType objectType, Projector projector, ProjectorProperties property, GameObject go)
        {
            var valueOriginal = ProjectorPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == property && x.ProjectorName == projector.NameFormatted())?.ValueOriginal;
            if (valueOriginal.IsNullOrEmpty())
                return null;
            return float.Parse(valueOriginal);
        }
        /// <summary>
        /// Get the value of the saved projector property value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="projector">Projector being modified</param>
        /// <param name="property">Property of the projector</param>
        /// <param name="go">GameObject the projector belongs to</param>
        /// <returns>Saved projector property value</returns>
        public float? GetProjectorPropertyValue(int slot, ObjectType objectType, Projector projector, ProjectorProperties property, GameObject go)
        {
            var valueOriginal = ProjectorPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == property && x.ProjectorName == projector.NameFormatted())?.Value;
            if (valueOriginal.IsNullOrEmpty())
                return null;
            return float.Parse(valueOriginal);
        }
        /// <summary>
        /// Remove the saved projector property value if one is saved and optionally also update the projector
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="projector">Projector being modified</param>
        /// <param name="property">Property of the projector</param>
        /// <param name="go">GameObject the projector belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the projector</param>
        public void RemoveProjectorProperty(int slot, ObjectType objectType, Projector projector, ProjectorProperties property, GameObject go, bool setProperty = true)
        {
            if (setProperty)
            {
                var original = GetProjectorPropertyValueOriginal(slot, objectType, projector, property, go);
                if (original != null)
                    MaterialAPI.SetProjectorProperty(go, projector.NameFormatted(), property, (float)original);
            }
            ProjectorPropertyList.RemoveAll(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == property && x.ProjectorName == projector.NameFormatted());
        }
        /// <summary>
        /// Add a renderer property to be saved and loaded with the card and optionally also update the renderer.
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="renderer">Renderer being modified</param>
        /// <param name="property">Property of the renderer</param>
        /// <param name="value">Value</param>
        /// <param name="go">GameObject the renderer belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the renderer</param>
        public void SetProjectorProperty(int slot, ObjectType objectType, Projector projector, ProjectorProperties property, float value, GameObject go, bool setProperty = true)
        {
            ProjectorProperty projectorProperty = ProjectorPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == property && x.ProjectorName == projector.NameFormatted());
            if (projectorProperty == null)
            {
                string valueOriginal = "";
                if (property == ProjectorProperties.FarClipPlane)
                    valueOriginal = projector.farClipPlane.ToString(CultureInfo.InvariantCulture);
                else if (property == ProjectorProperties.NearClipPlane)
                    valueOriginal = projector.nearClipPlane.ToString(CultureInfo.InvariantCulture);
                else if (property == ProjectorProperties.FieldOfView)
                    valueOriginal = projector.fieldOfView.ToString(CultureInfo.InvariantCulture);
                else if (property == ProjectorProperties.AspectRatio)
                    valueOriginal = projector.aspectRatio.ToString(CultureInfo.InvariantCulture);
                else if (property == ProjectorProperties.Orthographic)
                    valueOriginal = Convert.ToSingle(projector.orthographic).ToString(CultureInfo.InvariantCulture);
                else if (property == ProjectorProperties.OrthographicSize)
                    valueOriginal = projector.orthographicSize.ToString(CultureInfo.InvariantCulture);
                else if (property == ProjectorProperties.IgnoreCharaLayer)
                    valueOriginal = Convert.ToSingle(projector.ignoreLayers == (projector.ignoreLayers | (1 << 10))).ToString(CultureInfo.InvariantCulture);
                else if (property == ProjectorProperties.IgnoreMapLayer)
                    valueOriginal = Convert.ToSingle(projector.ignoreLayers == (projector.ignoreLayers | (1 << 11))).ToString(CultureInfo.InvariantCulture);

                if (valueOriginal != "")
                    ProjectorPropertyList.Add(new ProjectorProperty(objectType, GetCoordinateIndex(objectType), slot, projector.NameFormatted(), property, value.ToString(CultureInfo.InvariantCulture), valueOriginal));
            }
            else
            {
                if (value.ToString(CultureInfo.InvariantCulture) == projectorProperty.ValueOriginal)
                    RemoveProjectorProperty(slot, objectType, projector, property, go, false);
                else
                    projectorProperty.Value = value.ToString(CultureInfo.InvariantCulture);
            }

            if (setProperty)
                MaterialAPI.SetProjectorProperty(go, projector.NameFormatted(), property, value);
        }
        public IEnumerable<Projector> GetProjectorList(ObjectType objectType, GameObject gameObject)
        {
            //The body will never have a projector component attached
            //And returning all components from children will return every projector on a character
            if (objectType == ObjectType.Character)
                return new List<Projector>();
            return MaterialAPI.GetProjectorList(gameObject, true);
        }

        /// <summary>
        /// Add a renderer property to be saved and loaded with the card and optionally also update the renderer.
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="renderer">Renderer being modified</param>
        /// <param name="property">Property of the renderer</param>
        /// <param name="value">Value</param>
        /// <param name="go">GameObject the renderer belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the renderer</param>
        public void SetRendererProperty(int slot, ObjectType objectType, Renderer renderer, RendererProperties property, string value, GameObject go, bool setProperty = true)
        {
            RendererProperty rendererProperty = RendererPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == property && x.RendererName == renderer.NameFormatted());
            if (rendererProperty == null)
            {
                string valueOriginal = "";
                if (property == RendererProperties.Enabled)
                    valueOriginal = renderer.enabled ? "1" : "0";
                else if (property == RendererProperties.ReceiveShadows)
                    valueOriginal = renderer.receiveShadows ? "1" : "0";
                else if (property == RendererProperties.ShadowCastingMode)
                    valueOriginal = ((int)renderer.shadowCastingMode).ToString();
                else if (property == RendererProperties.UpdateWhenOffscreen)
                    if (renderer is SkinnedMeshRenderer meshRenderer)
                        valueOriginal = meshRenderer.updateWhenOffscreen ? "1" : "0";
                    else valueOriginal = "0";
                else if (property == RendererProperties.RecalculateNormals)
                    valueOriginal = "0"; // this property cannot be set by default

                if (valueOriginal != "")
                    RendererPropertyList.Add(new RendererProperty(objectType, GetCoordinateIndex(objectType), slot, renderer.NameFormatted(), property, value, valueOriginal));
            }
            else
            {
                if (value == rendererProperty.ValueOriginal)
                    RemoveRendererProperty(slot, objectType, renderer, property, go, false);
                else
                    rendererProperty.Value = value;
            }
            if (setProperty)
                MaterialAPI.SetRendererProperty(go, renderer.NameFormatted(), property, value);
        }
        /// <summary>
        /// Get the saved renderer property value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="renderer">Renderer being modified</param>
        /// <param name="property">Property of the renderer</param>
        /// <param name="go">GameObject the renderer belongs to</param>
        /// <returns>Saved renderer property value</returns>
        public string GetRendererPropertyValue(int slot, ObjectType objectType, Renderer renderer, RendererProperties property, GameObject go)
        {
            return RendererPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == property && x.RendererName == renderer.NameFormatted())?.Value;
        }
        /// <summary>
        /// Get the original value of the saved renderer property value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="renderer">Renderer being modified</param>
        /// <param name="property">Property of the renderer</param>
        /// <param name="go">GameObject the renderer belongs to</param>
        /// <returns>Saved renderer property value</returns>
        public string GetRendererPropertyValueOriginal(int slot, ObjectType objectType, Renderer renderer, RendererProperties property, GameObject go)
        {
            return RendererPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == property && x.RendererName == renderer.NameFormatted())?.ValueOriginal;
        }
        /// <summary>
        /// Remove the saved renderer property value if one is saved and optionally also update the renderer
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="renderer">Renderer being modified</param>
        /// <param name="property">Property of the renderer</param>
        /// <param name="go">GameObject the renderer belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the renderer</param>
        public void RemoveRendererProperty(int slot, ObjectType objectType, Renderer renderer, RendererProperties property, GameObject go, bool setProperty = true)
        {
            if (setProperty)
            {
                var original = GetRendererPropertyValueOriginal(slot, objectType, renderer, property, go);
                if (!original.IsNullOrEmpty())
                    MaterialAPI.SetRendererProperty(go, renderer.NameFormatted(), property, original);
                if (property == RendererProperties.RecalculateNormals)
                    MaterialEditorPlugin.Logger.LogMessage("Save and reload character or change outfits to reset normals.");
            }
            RendererPropertyList.RemoveAll(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == property && x.RendererName == renderer.NameFormatted());
        }

        /// <summary>
        /// Add a rename entry to be saved and loaded with the card and optionally also update the materials.
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="renderer">Renderer for which to rename the material</param>
        /// <param name="material">Material being modified</param>
        /// <param name="value">New name for the material</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void SetMaterialNameProperty(int slot, ObjectType objectType, Renderer renderer, Material material, string value, GameObject go, bool setProperty = true)
        {
            var materialProperty = MaterialNamePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Renderer == renderer.NameFormatted() && x.Value == material.name);
            if (materialProperty == null)
            {
                MaterialNamePropertyList.Add(new MaterialNameProperty(objectType, GetCoordinateIndex(objectType), slot, renderer, material, value));
                HandleMaterialNameChange(slot, objectType, renderer, material, value, go);
            }
            else
            {
                if (value.FormatShadingObjectName() == materialProperty.ValueOriginal.FormatShadingObjectName())
                    RemoveMaterialNameProperty(slot, objectType, renderer, material, go, false);
                else
                {
                    materialProperty.Value = value;
                    HandleMaterialNameChange(slot, objectType, renderer, material, value, go);
                }
            }
            if (setProperty)
                MaterialAPI.SetName(go, renderer.NameFormatted(), material.name, value);
        }
        /// <summary>
        /// Remove the saved material property value if one is saved and optionally also update the materials
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="renderer">Renderer for which to restore the original material name</param>
        /// <param name="material">Material to restore the name for</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void RemoveMaterialNameProperty(int slot, ObjectType objectType, Renderer renderer, Material material, GameObject go, bool setProperty = true)
        {
            if (setProperty)
            {
                var original = GetMaterialNamePropertyValueOriginal(slot, objectType, renderer, material, go);
                if (original != string.Empty)
                    MaterialAPI.SetName(go, renderer.NameFormatted(), material.name, original);
            }
            MaterialNamePropertyList.RemoveAll(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Renderer == renderer.NameFormatted() && x.Value == material.name);
        }
        /// <summary>
        /// Get the saved material property's current name or empty string if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="renderer">Renderer for which to check existence of modified material name</param>
        /// <param name="material">Material to check if it's been renamed</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property's current name or empty string if none is saved</returns>
        public string GetMaterialNamePropertyValue(int slot, ObjectType objectType, Renderer renderer, Material material, GameObject go)
        {
            return MaterialNamePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Renderer == renderer?.NameFormatted() && x.Value == material?.name)?.Value ?? string.Empty;
        }
        /// <summary>
        /// Get the saved material property's original name or empty string if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="renderer">Renderer for which to check existence of modified material name</param>
        /// <param name="material">Material to check if it's been renamed</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property's original value or null if none is saved</returns>
        public string GetMaterialNamePropertyValueOriginal(int slot, ObjectType objectType, Renderer renderer, Material material, GameObject go)
        {
            return MaterialNamePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Renderer == renderer?.NameFormatted() && x.Value == material?.name)?.ValueOriginal ?? string.Empty;
        }

        /// <summary>
        /// Add a float property to be saved and loaded with the card and optionally also update the materials.
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="value">Value</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void SetMaterialFloatProperty(int slot, ObjectType objectType, Material material, string propertyName, float value, GameObject go, bool setProperty = true)
        {
            var materialProperty = MaterialFloatPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
            if (materialProperty == null)
            {
                float valueOriginal = material.GetFloat($"_{propertyName}");
                MaterialFloatPropertyList.Add(new MaterialFloatProperty(objectType, GetCoordinateIndex(objectType), slot, material.NameFormatted(), propertyName, value.ToString(CultureInfo.InvariantCulture), valueOriginal.ToString(CultureInfo.InvariantCulture)));
            }
            else
            {
                if (value.ToString(CultureInfo.InvariantCulture) == materialProperty.ValueOriginal)
                    RemoveMaterialFloatProperty(slot, objectType, material, propertyName, go, false);
                else
                    materialProperty.Value = value.ToString(CultureInfo.InvariantCulture);
            }
            if (setProperty)
                SetFloat(go, material.NameFormatted(), propertyName, value);
        }
        /// <summary>
        /// Get the saved material property value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property value or null if none is saved</returns>
        public float? GetMaterialFloatPropertyValue(int slot, ObjectType objectType, Material material, string propertyName, GameObject go)
        {
            var value = MaterialFloatPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted())?.Value;
            if (value.IsNullOrEmpty())
                return null;
            return float.Parse(value ?? "");
        }
        /// <summary>
        /// Get the saved material property's original value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property's original value or null if none is saved</returns>
        public float? GetMaterialFloatPropertyValueOriginal(int slot, ObjectType objectType, Material material, string propertyName, GameObject go)
        {
            var valueOriginal = MaterialFloatPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted())?.ValueOriginal;
            if (valueOriginal.IsNullOrEmpty())
                return null;
            return float.Parse(valueOriginal ?? "");
        }
        /// <summary>
        /// Remove the saved material property value if one is saved and optionally also update the materials
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void RemoveMaterialFloatProperty(int slot, ObjectType objectType, Material material, string propertyName, GameObject go, bool setProperty = true)
        {
            if (setProperty)
            {
                var original = GetMaterialFloatPropertyValueOriginal(slot, objectType, material, propertyName, go);
                if (original != null)
                    SetFloat(go, material.NameFormatted(), propertyName, (float)original);
            }
            MaterialFloatPropertyList.RemoveAll(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
        }

        /// <summary>
        /// Add a keyword property to be saved and loaded with the card and optionally also update the materials.
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="value">Value</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void SetMaterialKeywordProperty(int slot, ObjectType objectType, Material material, string propertyName, bool value, GameObject go, bool setProperty = true)
        {
            var materialProperty = MaterialKeywordPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
            if (materialProperty == null)
            {
                bool valueOriginal = material.IsKeywordEnabled($"_{propertyName}");
                MaterialKeywordPropertyList.Add(new MaterialKeywordProperty(objectType, GetCoordinateIndex(objectType), slot, material.NameFormatted(), propertyName, value, valueOriginal));
            }
            else
            {
                if (value == materialProperty.ValueOriginal)
                    RemoveMaterialKeywordProperty(slot, objectType, material, propertyName, go, false);
                else
                    materialProperty.Value = true;
            }
            if (setProperty)
                SetKeyword(go, material.NameFormatted(), propertyName, value);
        }
        /// <summary>
        /// Get the saved material property value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property value or null if none is saved</returns>
        public bool? GetMaterialKeywordPropertyValue(int slot, ObjectType objectType, Material material, string propertyName, GameObject go)
        {
            return MaterialKeywordPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted())?.Value;
        }
        /// <summary>
        /// Get the saved material property's original value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property's original value or null if none is saved</returns>
        public bool? GetMaterialKeywordPropertyValueOriginal(int slot, ObjectType objectType, Material material, string propertyName, GameObject go)
        {
            return MaterialKeywordPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted())?.ValueOriginal;
        }
        /// <summary>
        /// Remove the saved material property value if one is saved and optionally also update the materials
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void RemoveMaterialKeywordProperty(int slot, ObjectType objectType, Material material, string propertyName, GameObject go, bool setProperty = true)
        {
            if (setProperty)
            {
                var original = GetMaterialKeywordPropertyValueOriginal(slot, objectType, material, propertyName, go);
                if (original != null)
                    SetKeyword(go, material.NameFormatted(), propertyName, (bool)original);
            }
            MaterialKeywordPropertyList.RemoveAll(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
        }

        /// <summary>
        /// Add a color property to be saved and loaded with the card and optionally also update the materials.
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="value">Value</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void SetMaterialColorProperty(int slot, ObjectType objectType, Material material, string propertyName, Color value, GameObject go, bool setProperty = true)
        {
            var colorProperty = MaterialColorPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
            if (colorProperty == null)
            {
                Color valueOriginal = material.GetColor($"_{propertyName}");
                MaterialColorPropertyList.Add(new MaterialColorProperty(objectType, GetCoordinateIndex(objectType), slot, material.NameFormatted(), propertyName, value, valueOriginal));
            }
            else
            {
                if (value == colorProperty.ValueOriginal)
                    RemoveMaterialColorProperty(slot, objectType, material, propertyName, go, false);
                else
                    colorProperty.Value = value;
            }
            if (setProperty)
                SetColor(go, material.NameFormatted(), propertyName, value);
        }
        /// <summary>
        /// Get the saved material property value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property value or null if none is saved</returns>
        public Color? GetMaterialColorPropertyValue(int slot, ObjectType objectType, Material material, string propertyName, GameObject go)
        {
            return MaterialColorPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted())?.Value;
        }
        /// <summary>
        /// Get the saved material property's original value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property's original value or null if none is saved</returns>
        public Color? GetMaterialColorPropertyValueOriginal(int slot, ObjectType objectType, Material material, string propertyName, GameObject go)
        {
            return MaterialColorPropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted())?.ValueOriginal;
        }
        /// <summary>
        /// Remove the saved material property value if one is saved and optionally also update the materials
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void RemoveMaterialColorProperty(int slot, ObjectType objectType, Material material, string propertyName, GameObject go, bool setProperty = true)
        {
            if (setProperty)
            {
                var original = GetMaterialColorPropertyValueOriginal(slot, objectType, material, propertyName, go);
                if (original != null)
                    SetColor(go, material.NameFormatted(), propertyName, (Color)original);
            }
            MaterialColorPropertyList.RemoveAll(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
        }

        /// <summary>
        /// Add a texture property to be saved and loaded with the card.
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="filePath">Path to the .png file on disk</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setTexInUpdate">Whether to wait for the next Update</param>
        public void SetMaterialTextureFromFile(int slot, ObjectType objectType, Material material, string propertyName, string filePath, GameObject go, bool setTexInUpdate = false)
        {
            if (!File.Exists(filePath)) return;

            if (setTexInUpdate)
            {
                FileToSet = filePath;
                PropertyToSet = propertyName;
                MatToSet = material;
                GameObjectToSet = go;
                SlotToSet = slot;
                ObjectTypeToSet = objectType;
            }
            else
            {
                var texBytes = File.ReadAllBytes(filePath);
                SetMaterialTexture(slot, objectType, material, propertyName, texBytes, go);
            }
        }

        /// <summary>
        /// Add a texture property to be saved and loaded with the card.
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="data">Byte array containing the texture data</param>
        /// <param name="go">GameObject the material belongs to</param>
        public void SetMaterialTexture(int slot, ObjectType objectType, Material material, string propertyName, byte[] data, GameObject go)
        {
            if (data == null) return;

            var texID = SetAndGetTextureID(data);
            var textureProperty = MaterialTexturePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
            if (textureProperty == null)
                MaterialTexturePropertyList.Add(textureProperty = new MaterialTextureProperty(objectType, GetCoordinateIndex(objectType), slot, material.NameFormatted(), propertyName, texID));
            else
                textureProperty.TexID = texID;

            textureProperty.TexAnimationDef = MEAnimationUtil.LoadAnimationDefFromBytes(texID, data, SetAndGetTextureID);
            SetTextureWithProperty(go, textureProperty);
        }
        /// <summary>
        /// Get the saved material property value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property value or null if none is saved</returns>
        public Texture GetMaterialTexture(int slot, ObjectType objectType, Material material, string propertyName, GameObject go)
        {
            var textureProperty = MaterialTexturePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
            if (textureProperty?.TexID != null)
                return TextureDictionary[(int)textureProperty.TexID].Texture;
            return null;
        }
        /// <summary>
        /// Get whether the texture has been changed
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>True if the texture has been modified, false if not</returns>
        public bool GetMaterialTextureOriginal(int slot, ObjectType objectType, Material material, string propertyName, GameObject go)
        {
            return MaterialTexturePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted())?.TexID == null;
        }
        /// <summary>
        /// Remove the saved material property value if one is saved and optionally also update the materials
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="displayMessage">Whether to display a message on screen telling the user to save and reload to refresh textures</param>
        public void RemoveMaterialTexture(int slot, ObjectType objectType, Material material, string propertyName, GameObject go, bool displayMessage = true)
        {
            var textureProperty = MaterialTexturePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
            if (textureProperty != null)
            {
                if (displayMessage)
                    MaterialEditorPlugin.Logger.LogMessage("Save and reload character or change outfits to refresh textures.");
                textureProperty.TexID = null;
                RemoveTexturePropertyIfNull(textureProperty);
            }
        }
        /// <summary>
        /// If TextureProperty is null, delete it.
        /// </summary>
        /// <param name="textureProperty"></param>
        private void RemoveTexturePropertyIfNull(MaterialTextureProperty textureProperty)
        {
            if (!textureProperty.NullCheck())
                return;
            MaterialTexturePropertyList.Remove(textureProperty);
            AnimationControllerMap.Remove(textureProperty);
        }

        /// <summary>
        /// Add a texture offset property to be saved and loaded with the card and optionally also update the materials.
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="value">Value</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void SetMaterialTextureOffset(int slot, ObjectType objectType, Material material, string propertyName, Vector2 value, GameObject go, bool setProperty = true)
        {
            var textureProperty = MaterialTexturePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
            if (textureProperty == null)
            {
                Vector2 valueOriginal = material.GetTextureOffset($"_{propertyName}");
                MaterialTexturePropertyList.Add(new MaterialTextureProperty(objectType, GetCoordinateIndex(objectType), slot, material.NameFormatted(), propertyName, offset: value, offsetOriginal: valueOriginal));
            }
            else
            {
                if (value == textureProperty.OffsetOriginal)
                    RemoveMaterialTextureOffset(slot, objectType, material, propertyName, go, false);
                else
                {
                    textureProperty.Offset = value;
                    if (textureProperty.OffsetOriginal == null)
                        textureProperty.OffsetOriginal = material.GetTextureOffset($"_{propertyName}");
                }
            }
            if (setProperty)
                SetTextureOffset(go, material.NameFormatted(), propertyName, value);
        }
        /// <summary>
        /// Get the saved material property value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property value or null if none is saved</returns>
        public Vector2? GetMaterialTextureOffset(int slot, ObjectType objectType, Material material, string propertyName, GameObject go)
        {
            return MaterialTexturePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted())?.Offset;
        }
        /// <summary>
        /// Get the saved material property's original value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property's original value or null if none is saved</returns>
        public Vector2? GetMaterialTextureOffsetOriginal(int slot, ObjectType objectType, Material material, string propertyName, GameObject go)
        {
            return MaterialTexturePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted())?.OffsetOriginal;
        }
        /// <summary>
        /// Remove the saved material property value if one is saved and optionally also update the materials
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void RemoveMaterialTextureOffset(int slot, ObjectType objectType, Material material, string propertyName, GameObject go, bool setProperty = true)
        {
            if (setProperty)
            {
                var original = GetMaterialTextureOffsetOriginal(slot, objectType, material, propertyName, go);
                if (original != null)
                    SetTextureOffset(go, material.NameFormatted(), propertyName, (Vector2)original);
            }

            var textureProperty = MaterialTexturePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
            if (textureProperty != null)
            {
                textureProperty.Offset = null;
                textureProperty.OffsetOriginal = null;
                RemoveTexturePropertyIfNull(textureProperty);
            }
        }

        /// <summary>
        /// Add a texture scale property to be saved and loaded with the card and optionally also update the materials.
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="value">Value</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void SetMaterialTextureScale(int slot, ObjectType objectType, Material material, string propertyName, Vector2 value, GameObject go, bool setProperty = true)
        {
            var textureProperty = MaterialTexturePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
            if (textureProperty == null)
            {
                Vector2 valueOriginal = material.GetTextureScale($"_{propertyName}");
                MaterialTexturePropertyList.Add(new MaterialTextureProperty(objectType, GetCoordinateIndex(objectType), slot, material.NameFormatted(), propertyName, scale: value, scaleOriginal: valueOriginal));
            }
            else
            {
                if (value == textureProperty.ScaleOriginal)
                    RemoveMaterialTextureScale(slot, objectType, material, propertyName, go, false);
                else
                {
                    textureProperty.Scale = value;
                    if (textureProperty.ScaleOriginal == null)
                        textureProperty.ScaleOriginal = material.GetTextureScale($"_{propertyName}");
                }
            }

            if (setProperty)
                SetTextureScale(go, material.NameFormatted(), propertyName, value);
        }
        /// <summary>
        /// Get the saved material property value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property value or null if none is saved</returns>
        public Vector2? GetMaterialTextureScale(int slot, ObjectType objectType, Material material, string propertyName, GameObject go)
        {
            return MaterialTexturePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted())?.Scale;
        }
        /// <summary>
        /// Get the saved material property's original value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved material property's original value or null if none is saved</returns>
        public Vector2? GetMaterialTextureScaleOriginal(int slot, ObjectType objectType, Material material, string propertyName, GameObject go)
        {
            return MaterialTexturePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted())?.ScaleOriginal;
        }
        /// <summary>
        /// Remove the saved material property value if one is saved and optionally also update the materials
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="propertyName">Property of the material without the leading underscore</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void RemoveMaterialTextureScale(int slot, ObjectType objectType, Material material, string propertyName, GameObject go, bool setProperty = true)
        {
            if (setProperty)
            {
                var original = GetMaterialTextureScaleOriginal(slot, objectType, material, propertyName, go);
                if (original != null)
                    SetTextureScale(go, material.NameFormatted(), propertyName, (Vector2)original);
            }

            var textureProperty = MaterialTexturePropertyList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.Property == propertyName && x.MaterialName == material.NameFormatted());
            if (textureProperty != null)
            {
                textureProperty.Scale = null;
                textureProperty.ScaleOriginal = null;
                RemoveTexturePropertyIfNull(textureProperty);
            }
        }

        /// <summary>
        /// Add a shader to be saved and loaded with the card and optionally also update the materials.
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="shaderName">Name of the shader to be saved, must be a shader that has been loaded by MaterialEditor</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void SetMaterialShader(int slot, ObjectType objectType, Material material, string shaderName, GameObject go, bool setProperty = true)
        {
            var materialProperty = MaterialShaderList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted());
            if (materialProperty == null)
            {
                string shaderNameOriginal = material.shader.NameFormatted();
                MaterialShaderList.Add(new MaterialShader(objectType, GetCoordinateIndex(objectType), slot, material.NameFormatted(), shaderName, shaderNameOriginal));
            }
            else
            {
                if (shaderName == materialProperty.ShaderNameOriginal)
                    RemoveMaterialShader(slot, objectType, material, go, false);
                else
                {
                    materialProperty.ShaderName = shaderName;
                    if (materialProperty.ShaderNameOriginal == null)
                        materialProperty.ShaderNameOriginal = material.shader.NameFormatted();
                }
            }

            if (setProperty)
            {
#if KK || EC || KKS
                if (objectType == ObjectType.Character && MaterialEditorPlugin.EyeMaterials.Contains(material.NameFormatted()))
                {
                    SetShader(go, material.NameFormatted(), shaderName, true);
                }
                else
#endif
                {
                    RemoveMaterialShaderRenderQueue(slot, objectType, material, go, false);
                    SetShader(go, material.NameFormatted(), shaderName);
                }
            }
        }

        /// <summary>
        /// Get the saved shader name or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved shader name or null if none is saved</returns>
        public string GetMaterialShader(int slot, ObjectType objectType, Material material, GameObject go)
        {
            return MaterialShaderList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted())?.ShaderName;
        }
        /// <summary>
        /// Get the saved shader name's original value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved shader name's original value or null if none is saved</returns>
        public string GetMaterialShaderOriginal(int slot, ObjectType objectType, Material material, GameObject go)
        {
            return MaterialShaderList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted())?.ShaderNameOriginal;
        }

        /// <summary>
        /// Remove the saved shader if one is saved and optionally also update the materials
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void RemoveMaterialShader(int slot, ObjectType objectType, Material material, GameObject go, bool setProperty = true)
        {
            if (setProperty)
            {
                var original = GetMaterialShaderOriginal(slot, objectType, material, go);
                if (!original.IsNullOrEmpty())
                {
#if KK || EC || KKS
                    if (objectType == ObjectType.Character && MaterialEditorPlugin.EyeMaterials.Contains(material.NameFormatted()))
                    {
                        SetShader(go, material.NameFormatted(), original, true);

                    }
                    else
#endif
                    {
                        SetShader(go, material.NameFormatted(), original);
                    }
                }
            }

            foreach (var materialProperty in MaterialShaderList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted()))
            {
                materialProperty.ShaderName = null;
                materialProperty.ShaderNameOriginal = null;
            }

            MaterialShaderList.RemoveAll(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted() && x.NullCheck());
        }

        /// <summary>
        /// Add a shader render queue to be saved and loaded with the card and optionally also update the materials.
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="renderQueue">Value</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void SetMaterialShaderRenderQueue(int slot, ObjectType objectType, Material material, int renderQueue, GameObject go, bool setProperty = true)
        {
            var materialProperty = MaterialShaderList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted());
            if (materialProperty == null)
            {
                int renderQueueOriginal = material.renderQueue;
                MaterialShaderList.Add(new MaterialShader(objectType, GetCoordinateIndex(objectType), slot, material.NameFormatted(), renderQueue, renderQueueOriginal));
            }
            else
            {
                int renderQueueOriginal;
                if (materialProperty.RenderQueueOriginal == null)
                    renderQueueOriginal = material.renderQueue;
                else
                    renderQueueOriginal = (int)materialProperty.RenderQueueOriginal;

                if (renderQueue == renderQueueOriginal)
                    RemoveMaterialShaderRenderQueue(slot, objectType, material, go, false);
                else
                {
                    materialProperty.RenderQueue = renderQueue;
                    if (materialProperty.RenderQueueOriginal == null)
                        materialProperty.RenderQueueOriginal = material.renderQueue;
                }
            }

            if (setProperty)
                SetRenderQueue(go, material.NameFormatted(), renderQueue);
        }
        /// <summary>
        /// Get the saved render queue or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved render queue or null if none is saved</returns>
        public int? GetMaterialShaderRenderQueue(int slot, ObjectType objectType, Material material, GameObject go)
        {
            return MaterialShaderList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted())?.RenderQueue;
        }
        /// <summary>
        /// Get the saved render queue's original value or null if none is saved
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <returns>Saved render queue's original value or null if none is saved</returns>
        public int? GetMaterialShaderRenderQueueOriginal(int slot, ObjectType objectType, Material material, GameObject go)
        {
            return MaterialShaderList.FirstOrDefault(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted())?.RenderQueueOriginal;
        }
        /// <summary>
        /// Remove the saved render queue if one is saved and optionally also update the materials
        /// </summary>
        /// <param name="slot">Slot of the clothing (0=tops, 1=bottoms, etc.), the hair (0=back, 1=front, etc.), or of the accessory. Ignored for other object types.</param>
        /// <param name="material">Material being modified. Also modifies all other materials of the same name.</param>
        /// <param name="go">GameObject the material belongs to</param>
        /// <param name="setProperty">Whether to also apply the value to the materials</param>
        public void RemoveMaterialShaderRenderQueue(int slot, ObjectType objectType, Material material, GameObject go, bool setProperty = true)
        {
            if (setProperty)
            {
                var original = GetMaterialShaderRenderQueueOriginal(slot, objectType, material, go);
                if (original != null)
                    SetRenderQueue(go, material.NameFormatted(), original);
            }

            foreach (var materialProperty in MaterialShaderList.Where(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted()))
            {
                materialProperty.RenderQueue = null;
                materialProperty.RenderQueueOriginal = null;
            }

            MaterialShaderList.RemoveAll(x => x.ObjectType == objectType && x.CoordinateIndex == GetCoordinateIndex(objectType) && x.Slot == slot && x.MaterialName == material.NameFormatted() && x.NullCheck());
        }

        /// <summary>
        /// Get the coordinate index based on object type, hair and character return 0, clothes and accessories return CurrentCoordinateIndex
        /// </summary>
        private int GetCoordinateIndex(ObjectType objectType)
        {
#if KK || KKS
            if (objectType == ObjectType.Accessory || objectType == ObjectType.Clothing)
                return CurrentCoordinateIndex;
#endif
            return 0;
        }
        #endregion

        private bool coordinateChanging;
        /// <summary>
        /// Whether the coordinate is being changed this Update. Used by methods that happen later in the update. If set, reverts to false on next Update.
        /// </summary>
        public bool CoordinateChanging
        {
            get => coordinateChanging;
            set
            {
                coordinateChanging = value;
                ChaControl.StartCoroutine(Reset());
                IEnumerator Reset()
                {
                    yield return null;
                    coordinateChanging = false;
                }
            }
        }

        private bool accessorySelectedSlotChanging;
        /// <summary>
        /// Whether the selected accessory slot is being changed this Update. Used by methods that happen later in the update. If set, reverts to false on next Update.
        /// </summary>
        public bool AccessorySelectedSlotChanging
        {
            get => accessorySelectedSlotChanging;
            set
            {
                accessorySelectedSlotChanging = value;
                ChaControl.StartCoroutine(Reset());
                IEnumerator Reset()
                {
                    yield return null;
                    accessorySelectedSlotChanging = false;
                }
            }
        }

        private bool clothesChanging;
        /// <summary>
        /// Whether the clothes are being changed this Update. Used by methods that happen later in the update. If set, reverts to false on next Update.
        /// </summary>
        public bool ClothesChanging
        {
            get => clothesChanging;
            set
            {
                clothesChanging = value;
                ChaControl.StartCoroutine(Reset());
                IEnumerator Reset()
                {
                    yield return null;
                    clothesChanging = false;
                }
            }
        }

        private bool characterLoading;
        /// <summary>
        /// Whether the character is being changed this Update. Used by methods that happen later in the update. If set, reverts to false on next Update.
        /// </summary>
        public bool CharacterLoading
        {
            get => characterLoading;
            set
            {
                characterLoading = value;
                ChaControl.StartCoroutine(Reset());
                IEnumerator Reset()
                {
                    yield return null;
                    characterLoading = false;
                }
            }
        }

        private bool refreshingTextures;
        /// <summary>
        /// Whether the overlay plugin is refreshing textures this Update. Used by methods that happen later in the update. If set, reverts to false on next Update.
        /// </summary>
        public bool RefreshingTextures
        {
            get => refreshingTextures;
            set
            {
                refreshingTextures = value;
                ChaControl.StartCoroutine(Reset());
                IEnumerator Reset()
                {
                    yield return null;
                    refreshingTextures = false;
                }
            }
        }

        private bool customClothesOverride;
        /// <summary>
        /// Override flag set to distinguish between clothes being changed via character maker and clothes changed by changing outfit slots, loading the character, or other methods.
        /// Used by methods that happen later in the update. If set, reverts to false on next Update.
        /// </summary>
        public bool CustomClothesOverride
        {
            get => customClothesOverride;
            set
            {
                customClothesOverride = value;
                ChaControl.StartCoroutine(Reset());
                IEnumerator Reset()
                {
                    yield return null;
                    customClothesOverride = false;
                }
            }
        }

        private GameObject FindGameObject(ObjectType objectType, int slot)
        {
            if (objectType == ObjectType.Clothing)
                return ChaControl.GetClothes(slot);
            if (objectType == ObjectType.Accessory)
            {
                var acc = ChaControl.GetAccessoryObject(slot);
                if (acc != null)
                    return acc;
            }
            if (objectType == ObjectType.Hair)
            {
                var hair = ChaControl.GetHair(slot);
                if (hair != null)
                    return hair.gameObject;
            }
            if (objectType == ObjectType.Character)
                return ChaControl.gameObject;
            return null;
        }

        /// <summary>
        /// Purge unused textures from TextureDictionary
        /// </summary>
        protected int PurgeUnusedTextures()
        {
            if (TextureDictionary.Count <= 0)
                return 0;

            HashSet<int> unuseds = new HashSet<int>(TextureDictionary.Keys);

            //Remove textures in use
            for (int i = 0; i < MaterialTexturePropertyList.Count; ++i)
            {
                var prop = MaterialTexturePropertyList[i];
                var texID = prop.TexID;
                if (texID.HasValue)
                    unuseds.Remove(texID.Value);

                if (prop.TexAnimationDef != null)
                {
                    var frames = prop.TexAnimationDef.frames;
                    for (int j = 0; j < frames.Length; ++j)
                        unuseds.Remove(frames[j].texID);
                }
            }

            foreach (var texID in unuseds)
            {
                TextureDictionary[texID].Dispose();
                TextureDictionary.Remove(texID);
            }
            return unuseds.Count;
        }

#if KK || KKS

        /// <summary>
        /// Purge coordinate properties that reference a coordinate that no longer exists
        /// </summary>
        internal void PurgeUnusedCoordinates()
        {
            RendererPropertyList.RemoveAll(x => ChaControl.chaFile.coordinate.ElementAtOrDefault(x.CoordinateIndex) == null);
            ProjectorPropertyList.RemoveAll(x => ChaControl.chaFile.coordinate.ElementAtOrDefault(x.CoordinateIndex) == null);
            MaterialNamePropertyList.RemoveAll(x => ChaControl.chaFile.coordinate.ElementAtOrDefault(x.CoordinateIndex) == null);
            MaterialFloatPropertyList.RemoveAll(x => ChaControl.chaFile.coordinate.ElementAtOrDefault(x.CoordinateIndex) == null);
            MaterialColorPropertyList.RemoveAll(x => ChaControl.chaFile.coordinate.ElementAtOrDefault(x.CoordinateIndex) == null);
            MaterialKeywordPropertyList.RemoveAll(x => ChaControl.chaFile.coordinate.ElementAtOrDefault(x.CoordinateIndex) == null);
            MaterialTexturePropertyList.RemoveAll(x => ChaControl.chaFile.coordinate.ElementAtOrDefault(x.CoordinateIndex) == null);
            MaterialShaderList.RemoveAll(x => ChaControl.chaFile.coordinate.ElementAtOrDefault(x.CoordinateIndex) == null);
            MaterialCopyList.RemoveAll(x => ChaControl.chaFile.coordinate.ElementAtOrDefault(x.CoordinateIndex) == null);
        }
#endif

        internal void PurgeOrphanedProperties()
        {
            int removedCount = 0;

            for (var i = 0; i < ChaControl.GetClothes().Length; i++)
                removeProperties(ObjectType.Clothing, i, ChaControl.GetClothes()[i]);
            for (var i = 0; i < ChaControl.GetAccessoryObjects().Length; i++)
                removeProperties(ObjectType.Accessory, i, ChaControl.GetAccessoryObjects()[i]);
            for (var i = 0; i < ChaControl.GetHair().Length; i++)
                removeProperties(ObjectType.Hair, i, ChaControl.GetHair()[i]);
            //The same is not done for the body because some properties are not exposed, while technically still there and used
            //An example would be the face alpha mask not being exposed in koikatsu's v+ shaders, while still being applied if set in a shader that does expose it

            void removeProperties(ObjectType objectType, int slot, GameObject go)
            {
                if (go == null) return;
                var renderers = GetRendererList(go);
                if (renderers == null) return;

                var materialNames = renderers.SelectMany(x => x.materials).Select(x => x.NameFormatted()).ToList();
                var projectors = GetProjectorList(objectType, go);
                materialNames.AddRange(projectors.Select(x => x.material.NameFormatted()));

                var materialPropertiesDict = renderers
                    .SelectMany(x => x.materials)
                    .GroupBy(x => x.NameFormatted())
                    .Select(x => x.First())
                    .ToDictionary(
                        x => x.NameFormatted(),
                        x => XMLShaderProperties[XMLShaderProperties.ContainsKey(x.shader.NameFormatted()) ? x.shader.NameFormatted() : "default"].Select(i => i.Key)
                );

                removedCount += ProjectorPropertyList.RemoveAll(
                    x => x.CoordinateIndex == CurrentCoordinateIndex
                    && x.Slot == slot
                    && x.ObjectType == objectType
                    && !projectors.Select(projector => projector.NameFormatted()).Contains(x.ProjectorName)
                );
                removedCount += RendererPropertyList.RemoveAll(
                    x => x.CoordinateIndex == CurrentCoordinateIndex
                    && x.Slot == slot
                    && x.ObjectType == objectType
                    && !renderers.Select(rend => rend.NameFormatted()).Contains(x.RendererName)
                );
                removedCount += MaterialNamePropertyList.RemoveAll(
                    x => x.CoordinateIndex == CurrentCoordinateIndex
                    && x.Slot == slot
                    && x.ObjectType == objectType
                    && !materialNames.Contains(x.MaterialName.FormatShadingObjectName())
                    && !materialNames.Contains(x.Value)
                );
                removedCount += MaterialFloatPropertyList.RemoveAll(
                    x => x.CoordinateIndex == CurrentCoordinateIndex
                    && x.Slot == slot
                    && x.ObjectType == objectType
                    && (
                        !materialNames.Contains(x.MaterialName)
                        || !materialPropertiesDict.ContainsKey(x.MaterialName)
                        || !materialPropertiesDict[x.MaterialName].Contains(x.Property)
                    )
                );
                removedCount += MaterialColorPropertyList.RemoveAll(
                    x => x.CoordinateIndex == CurrentCoordinateIndex
                    && x.Slot == slot
                    && x.ObjectType == objectType
                    && (
                        !materialNames.Contains(x.MaterialName)
                        || !materialPropertiesDict.ContainsKey(x.MaterialName)
                        || !materialPropertiesDict[x.MaterialName].Contains(x.Property)
                    )
                );
                removedCount += MaterialKeywordPropertyList.RemoveAll(
                    x => x.CoordinateIndex == CurrentCoordinateIndex
                    && x.Slot == slot
                    && x.ObjectType == objectType
                    && (
                        !materialNames.Contains(x.MaterialName)
                        || !materialPropertiesDict.ContainsKey(x.MaterialName)
                        || !materialPropertiesDict[x.MaterialName].Contains(x.Property)
                    )
                );
                removedCount += MaterialTexturePropertyList.RemoveAll(
                    x => x.CoordinateIndex == CurrentCoordinateIndex
                    && x.Slot == slot
                    && x.ObjectType == objectType
                    && (
                        !materialNames.Contains(x.MaterialName)
                        || !materialPropertiesDict.ContainsKey(x.MaterialName)
                        || !materialPropertiesDict[x.MaterialName].Contains(x.Property)
                    )
                );
                removedCount += MaterialShaderList.RemoveAll(
                    x => x.CoordinateIndex == CurrentCoordinateIndex
                    && x.Slot == slot
                    && x.ObjectType == objectType
                    && !materialNames.Contains(x.MaterialName)
                );
                removedCount += MaterialCopyList.RemoveAll(
                    x => x.CoordinateIndex == CurrentCoordinateIndex
                    && x.Slot == slot
                    && x.ObjectType == objectType
                    && !materialNames.Contains(x.MaterialName)
                );
            }
            var purgedTextures = PurgeUnusedTextures();
            if (purgedTextures == 0)
                MaterialEditorPluginBase.Logger.LogMessage($"Removed {removedCount} orphaned propertie(s)");
            else
                MaterialEditorPluginBase.Logger.LogMessage($"Removed {removedCount} orphaned propertie(s) and {purgedTextures} orphaned texture(s)");
        }

        /// <summary>
        /// Purge unused animation
        /// </summary>
        private void PurgeUnusedAnimation()
        {
            MEAnimationUtil.PurgeUnusedAnimation(AnimationControllerMap, MaterialTexturePropertyList);
        }

        /// <summary>
        /// Initialization of animation controllers
        /// </summary>
        private static void InitAnimationController()
        {
            MEAnimationController.UpdateTexture = SetTextureForAnimation;
            MEAnimationController.GetTexID = GetTexIDWithAnimation;
        }

        /// <summary>
        /// Get texture ID from MaterialTextureProperty
        /// </summary>
        private static int? GetTexIDWithAnimation(MaterialTextureProperty property)
        {
            return property.TexID;
        }

        /// <summary>
        /// Set of textures for animation
        /// </summary>
        private static void SetTextureForAnimation(MaterialEditorCharaController controller, GameObject go, MaterialTextureProperty property, int texID)
        {
            if (!controller.TextureDictionary.TryGetValue(texID, out var tex))
                return;

            SetTexture(go, property.MaterialName, property.Property, tex.Texture);
        }

        /// <summary>
        /// Type of object, used for saving MaterialEditor data.
        /// </summary>
        public enum ObjectType
        {
            /// <summary>
            /// Unknown type, things should never be of this type
            /// </summary>
            Unknown,
            /// <summary>
            /// Clothing
            /// </summary>
            Clothing,
            /// <summary>
            /// Accessory
            /// </summary>
            Accessory,
            /// <summary>
            /// Hair
            /// </summary>
            Hair,
            /// <summary>
            /// Parts of a character
            /// </summary>
            Character
        };

        /// <summary>
        /// Data storage class for renderer properties
        /// </summary>
        [Serializable]
        [MessagePackObject]
        public class RendererProperty
        {
            /// <summary>
            /// Type of the object
            /// </summary>
            [Key("ObjectType")]
            public ObjectType ObjectType;
            /// <summary>
            /// Coordinate index, always 0 except in Koikatsu
            /// </summary>
            [Key("CoordinateIndex")]
            public int CoordinateIndex;
            /// <summary>
            /// Slot of the accessory, hair, or clothing
            /// </summary>
            [Key("Slot")]
            public int Slot;
            /// <summary>
            /// Name of the renderer
            /// </summary>
            [Key("RendererName")]
            public string RendererName;
            /// <summary>
            /// Property type
            /// </summary>
            [Key("Property")]
            public RendererProperties Property;
            /// <summary>
            /// Value
            /// </summary>
            [Key("Value")]
            public string Value;
            /// <summary>
            /// Original value
            /// </summary>
            [Key("ValueOriginal")]
            public string ValueOriginal;

            /// <summary>
            /// Data storage class for renderer properties
            /// </summary>
            /// <param name="objectType">Type of the object</param>
            /// <param name="coordinateIndex">Coordinate index, always 0 except in Koikatsu</param>
            /// <param name="slot">Slot of the accessory, hair, or clothing</param>
            /// <param name="rendererName">Name of the renderer</param>
            /// <param name="property">Property type</param>
            /// <param name="value">Value</param>
            /// <param name="valueOriginal">Original</param>
            public RendererProperty(ObjectType objectType, int coordinateIndex, int slot, string rendererName, RendererProperties property, string value, string valueOriginal)
            {
                ObjectType = objectType;
                CoordinateIndex = coordinateIndex;
                Slot = slot;
                RendererName = rendererName.FormatShadingObjectName();
                Property = property;
                Value = value;
                ValueOriginal = valueOriginal;
            }
        }

        [Serializable]
        [MessagePackObject]
        public class MaterialNameProperty
        {
            /// <summary>
            /// Type of the object
            /// </summary>
            [Key("ObjectType")]
            public ObjectType ObjectType;
            /// <summary>
            /// Coordinate index, always 0 except in Koikatsu
            /// </summary>
            [Key("CoordinateIndex")]
            public int CoordinateIndex;
            /// <summary>
            /// Slot of the accessory, hair, or clothing
            /// </summary>
            [Key("Slot")]
            public int Slot;
            /// <summary>
            /// Name of the renderer
            /// </summary>
            [Key("Renderer")]
            public string Renderer;
            /// <summary>
            /// Name of the material
            /// </summary>
            [Key("MaterialName")]
            public string MaterialName;
            /// <summary>
            /// Value
            /// </summary>
            [Key("Value")]
            public string Value;
            /// <summary>
            /// Original value
            /// </summary>
            [Key("ValueOriginal")]
            public string ValueOriginal;

            /// <summary>
            /// Data storage class for name properties
            /// </summary>
            /// <param name="objectType">Type of the object</param>
            /// <param name="coordinateIndex">Coordinate index, always 0 except in Koikatsu</param>
            /// <param name="slot">Slot of the accessory, hair, or clothing</param>
            /// <param name="renderer">Renderer being modified</param>
            /// <param name="material">Material being renamed</param>
            /// <param name="value">New name for the material</param>
            public MaterialNameProperty(ObjectType objectType, int coordinateIndex, int slot, Renderer renderer, Material material, string value)
            {
                ObjectType = objectType;
                CoordinateIndex = coordinateIndex;
                Slot = slot;
                Renderer = renderer.NameFormatted();
                MaterialName = material.name;
                Value = value;
                ValueOriginal = material.name;
            }
            /// <summary>
            /// Data storage class for name properties
            /// </summary>
            /// <param name="objectType">Type of the object</param>
            /// <param name="coordinateIndex">Coordinate index, always 0 except in Koikatsu</param>
            /// <param name="slot">Slot of the accessory, hair, or clothing</param>
            /// <param name="renderer">NameFormatted() name of the Renderer being modified</param>
            /// <param name="materialName">Raw, unmodified name of the Material being renamed</param>
            /// <param name="value">New name for the material</param>
            [SerializationConstructor]
            public MaterialNameProperty(ObjectType objectType, int coordinateIndex, int slot, string renderer, string materialName, string value)
            {
                ObjectType = objectType;
                CoordinateIndex = coordinateIndex;
                Slot = slot;
                Renderer = renderer;
                MaterialName = materialName;
                Value = value;
                ValueOriginal = materialName;
            }
        }

        /// <summary>
        /// Data storage class for keyword properties
        /// </summary>
        [Serializable]
        [MessagePackObject]

        public class MaterialKeywordProperty
        {
            /// <summary>
            /// Type of the object
            /// </summary>
            [Key("ObjectType")]
            public ObjectType ObjectType;
            /// <summary>
            /// Coordinate index, always 0 except in Koikatsu
            /// </summary>
            [Key("CoordinateIndex")]
            public int CoordinateIndex;
            /// <summary>
            /// Slot of the accessory, hair, or clothing
            /// </summary>
            [Key("Slot")]
            public int Slot;
            /// <summary>
            /// Name of the material
            /// </summary>
            [Key("MaterialName")]
            public string MaterialName;
            /// <summary>
            /// Name of the property
            /// </summary>
            [Key("Property")]
            public string Property;
            /// <summary>
            /// Value
            /// </summary>
            [Key("Value")]
            public bool Value;
            /// <summary>
            /// Original value
            /// </summary>
            [Key("ValueOriginal")]
            public bool ValueOriginal;

            /// <summary>
            /// Data storage class for keyword properties
            /// </summary>
            /// <param name="objectType">Type of the object</param>
            /// <param name="coordinateIndex">Coordinate index, always 0 except in Koikatsu</param>
            /// <param name="slot">Slot of the accessory, hair, or clothing</param>
            /// <param name="materialName">Name of the material</param>
            /// <param name="property">Name of the property</param>
            /// <param name="value">Value</param>
            /// <param name="valueOriginal">Original value</param>
            public MaterialKeywordProperty(ObjectType objectType, int coordinateIndex, int slot, string materialName, string property, bool value, bool valueOriginal)
            {
                ObjectType = objectType;
                CoordinateIndex = coordinateIndex;
                Slot = slot;
                MaterialName = materialName.FormatShadingObjectName();
                Property = property;
                Value = value;
                ValueOriginal = valueOriginal;
            }
        }

        /// <summary>
        /// Data storage class for float properties
        /// </summary>
        [Serializable]
        [MessagePackObject]
        public class MaterialFloatProperty
        {
            /// <summary>
            /// Type of the object
            /// </summary>
            [Key("ObjectType")]
            public ObjectType ObjectType;
            /// <summary>
            /// Coordinate index, always 0 except in Koikatsu
            /// </summary>
            [Key("CoordinateIndex")]
            public int CoordinateIndex;
            /// <summary>
            /// Slot of the accessory, hair, or clothing
            /// </summary>
            [Key("Slot")]
            public int Slot;
            /// <summary>
            /// Name of the material
            /// </summary>
            [Key("MaterialName")]
            public string MaterialName;
            /// <summary>
            /// Name of the property
            /// </summary>
            [Key("Property")]
            public string Property;
            /// <summary>
            /// Value
            /// </summary>
            [Key("Value")]
            public string Value;
            /// <summary>
            /// Original value
            /// </summary>
            [Key("ValueOriginal")]
            public string ValueOriginal;

            /// <summary>
            /// Data storage class for float properties
            /// </summary>
            /// <param name="objectType">Type of the object</param>
            /// <param name="coordinateIndex">Coordinate index, always 0 except in Koikatsu</param>
            /// <param name="slot">Slot of the accessory, hair, or clothing</param>
            /// <param name="materialName">Name of the material</param>
            /// <param name="property">Name of the property</param>
            /// <param name="value">Value</param>
            /// <param name="valueOriginal">Original value</param>
            public MaterialFloatProperty(ObjectType objectType, int coordinateIndex, int slot, string materialName, string property, string value, string valueOriginal)
            {
                ObjectType = objectType;
                CoordinateIndex = coordinateIndex;
                Slot = slot;
                MaterialName = materialName.FormatShadingObjectName();
                Property = property;
                Value = value;
                ValueOriginal = valueOriginal;
            }
        }

        /// <summary>
        /// Data storage class for color properties
        /// </summary>
        [Serializable]
        [MessagePackObject]
        public class MaterialColorProperty
        {
            /// <summary>
            /// Type of the object
            /// </summary>
            [Key("ObjectType")]
            public ObjectType ObjectType;
            /// <summary>
            /// Coordinate index, always 0 except in Koikatsu
            /// </summary>
            [Key("CoordinateIndex")]
            public int CoordinateIndex;
            /// <summary>
            /// Slot of the accessory, hair, or clothing
            /// </summary>
            [Key("Slot")]
            public int Slot;
            /// <summary>
            /// Name of the material
            /// </summary>
            [Key("MaterialName")]
            public string MaterialName;
            /// <summary>
            /// Name of the property
            /// </summary>
            [Key("Property")]
            public string Property;
            /// <summary>
            /// Value
            /// </summary>
            [Key("Value")]
            public Color Value;
            /// <summary>
            /// Original value
            /// </summary>
            [Key("ValueOriginal")]
            public Color ValueOriginal;

            /// <summary>
            /// Data storage class for color properties
            /// </summary>
            /// <param name="objectType">Type of the object</param>
            /// <param name="coordinateIndex">Coordinate index, always 0 except in Koikatsu</param>
            /// <param name="slot">Slot of the accessory, hair, or clothing</param>
            /// <param name="materialName">Name of the material</param>
            /// <param name="property">Name of the property</param>
            /// <param name="value">Value</param>
            /// <param name="valueOriginal">Original value</param>
            public MaterialColorProperty(ObjectType objectType, int coordinateIndex, int slot, string materialName, string property, Color value, Color valueOriginal)
            {
                ObjectType = objectType;
                CoordinateIndex = coordinateIndex;
                Slot = slot;
                MaterialName = materialName.FormatShadingObjectName();
                Property = property;
                Value = value;
                ValueOriginal = valueOriginal;
            }
        }

        /// <summary>
        /// Data storage class for texture properties
        /// </summary>
        [Serializable]
        [MessagePackObject]
        public class MaterialTextureProperty
        {
            /// <summary>
            /// Type of the object
            /// </summary>
            [Key("ObjectType")]
            public ObjectType ObjectType;
            /// <summary>
            /// Coordinate index, always 0 except in Koikatsu
            /// </summary>
            [Key("CoordinateIndex")]
            public int CoordinateIndex;
            /// <summary>
            /// Slot of the accessory, hair, or clothing
            /// </summary>
            [Key("Slot")]
            public int Slot;
            /// <summary>
            /// Name of the material
            /// </summary>
            [Key("MaterialName")]
            public string MaterialName;
            /// <summary>
            /// Name of the property
            /// </summary>
            [Key("Property")]
            public string Property;
            /// <summary>
            /// ID of the texture as stored in the texture dictionary
            /// </summary>
            [Key("TexID")]
            public int? TexID;
            /// <summary>
            /// Texture offset value
            /// </summary>
            [Key("Offset")]
            public Vector2? Offset;
            /// <summary>
            /// Texture offset original value
            /// </summary>
            [Key("OffsetOriginal")]
            public Vector2? OffsetOriginal;
            /// <summary>
            /// Texture scale value
            /// </summary>
            [Key("Scale")]
            public Vector2? Scale;
            /// <summary>
            /// Texture scale original value
            /// </summary>
            [Key("ScaleOriginal")]
            public Vector2? ScaleOriginal;
            /// <summary>
            /// Texture Animation Definition
            /// </summary>
            [Key("TexAnimationDef")]
            public MEAnimationDefine TexAnimationDef;

            /// <summary>
            /// Data storage class for texture properties
            /// </summary>
            /// <param name="objectType">Type of the object</param>
            /// <param name="coordinateIndex">Coordinate index, always 0 except in Koikatsu</param>
            /// <param name="slot">Slot of the accessory, hair, or clothing</param>
            /// <param name="materialName">Name of the material</param>
            /// <param name="property">Name of the property</param>
            /// <param name="texID">ID of the texture as stored in the texture dictionary</param>
            /// <param name="offset">Texture offset value</param>
            /// <param name="offsetOriginal">Texture offset original value</param>
            /// <param name="scale">Texture scale value</param>
            /// <param name="scaleOriginal">Texture scale original value</param>
            /// <param name="texAnimationDef">Texture animation define</param>
            public MaterialTextureProperty(ObjectType objectType, int coordinateIndex, int slot, string materialName, string property, int? texID = null, Vector2? offset = null, Vector2? offsetOriginal = null, Vector2? scale = null, Vector2? scaleOriginal = null, MEAnimationDefine texAnimationDef = null)
            {
                ObjectType = objectType;
                CoordinateIndex = coordinateIndex;
                Slot = slot;
                MaterialName = materialName.FormatShadingObjectName();
                Property = property;
                TexID = texID;
                Offset = offset;
                OffsetOriginal = offsetOriginal;
                Scale = scale;
                ScaleOriginal = scaleOriginal;
                TexAnimationDef = texAnimationDef;
            }

            /// <summary>
            /// Check if any of a subset of properties is null. Which either make it safe to remove or a broken property.
            /// Both cases make the TextureProperty safe for removal
            /// </summary>
            /// <returns></returns>
            public bool NullCheck() 
            {
                // These become null when an animated texture is removed
                var safeToRemove = TexID == null && Offset == null && Scale == null;
                // These should never be null, and the property will never work if they are (and can cause issues)
                var brokenProperty = Property == null || MaterialName == null;
                return safeToRemove || brokenProperty;
            }
        }

        /// <summary>
        /// Data storage class for shader data
        /// </summary>
        [Serializable]
        [MessagePackObject]
        public class MaterialShader
        {
            /// <summary>
            /// Type of the object
            /// </summary>
            [Key("ObjectType")]
            public ObjectType ObjectType;
            /// <summary>
            /// Coordinate index, always 0 except in Koikatsu
            /// </summary>
            [Key("CoordinateIndex")]
            public int CoordinateIndex;
            /// <summary>
            /// Slot of the accessory, hair, or clothing
            /// </summary>
            [Key("Slot")]
            public int Slot;
            /// <summary>
            /// Name of the material
            /// </summary>
            [Key("MaterialName")]
            public string MaterialName;
            /// <summary>
            /// Name of the shader
            /// </summary>
            [Key("ShaderName")]
            public string ShaderName;
            /// <summary>
            /// Name of the original shader
            /// </summary>
            [Key("ShaderNameOriginal")]
            public string ShaderNameOriginal;
            /// <summary>
            /// Render queue
            /// </summary>
            [Key("RenderQueue")]
            public int? RenderQueue;
            /// <summary>
            /// Original render queue
            /// </summary>
            [Key("RenderQueueOriginal")]
            public int? RenderQueueOriginal;

            /// <summary>
            /// Data storage class for shader data
            /// </summary>
            /// <param name="objectType">Type of the object</param>
            /// <param name="coordinateIndex">Coordinate index, always 0 except in Koikatsu</param>
            /// <param name="slot">Slot of the accessory, hair, or clothing</param>
            /// <param name="materialName">Name of the material</param>
            /// <param name="shaderName">Name of the shader</param>
            /// <param name="shaderNameOriginal">Name of the original shader</param>
            /// <param name="renderQueue">Render queue</param>
            /// <param name="renderQueueOriginal">Original render queue</param>
            public MaterialShader(ObjectType objectType, int coordinateIndex, int slot, string materialName, string shaderName, string shaderNameOriginal, int? renderQueue, int? renderQueueOriginal)
            {
                ObjectType = objectType;
                CoordinateIndex = coordinateIndex;
                Slot = slot;
                MaterialName = materialName.FormatShadingObjectName();
                ShaderName = shaderName;
                ShaderNameOriginal = shaderNameOriginal;
                RenderQueue = renderQueue;
                RenderQueueOriginal = renderQueueOriginal;
            }
            /// <summary>
            /// Data storage class for shader data
            /// </summary>
            /// <param name="objectType">Type of the object</param>
            /// <param name="coordinateIndex">Coordinate index, always 0 except in Koikatsu</param>
            /// <param name="slot">Slot of the accessory, hair, or clothing</param>
            /// <param name="materialName">Name of the material</param>
            /// <param name="shaderName">Name of the shader</param>
            /// <param name="shaderNameOriginal">Name of the original shader</param>
            public MaterialShader(ObjectType objectType, int coordinateIndex, int slot, string materialName, string shaderName, string shaderNameOriginal)
            {
                ObjectType = objectType;
                CoordinateIndex = coordinateIndex;
                Slot = slot;
                MaterialName = materialName.FormatShadingObjectName();
                ShaderName = shaderName;
                ShaderNameOriginal = shaderNameOriginal;
            }
            /// <summary>
            /// Data storage class for shader data
            /// </summary>
            /// <param name="objectType">Type of the object</param>
            /// <param name="coordinateIndex">Coordinate index, always 0 except in Koikatsu</param>
            /// <param name="slot">Slot of the accessory, hair, or clothing</param>
            /// <param name="materialName">Name of the material</param>
            /// <param name="renderQueue">Render queue</param>
            /// <param name="renderQueueOriginal">Original render queue</param>
            public MaterialShader(ObjectType objectType, int coordinateIndex, int slot, string materialName, int? renderQueue, int? renderQueueOriginal)
            {
                ObjectType = objectType;
                CoordinateIndex = coordinateIndex;
                Slot = slot;
                MaterialName = materialName.FormatShadingObjectName();
                RenderQueue = renderQueue;
                RenderQueueOriginal = renderQueueOriginal;
            }

            /// <summary>
            /// Check if the shader name and render queue are both null. Safe to delete this data if true.
            /// </summary>
            /// <returns></returns>
            public bool NullCheck() => ShaderName.IsNullOrEmpty() && RenderQueue == null;
        }

        /// <summary>
        /// Data storage class for material copy info
        /// </summary>
        [Serializable]
        [MessagePackObject]
        public class MaterialCopy
        {
            /// <summary>
            /// Type of the object
            /// </summary>
            [Key("ObjectType")]
            public ObjectType ObjectType;
            /// <summary>
            /// Coordinate index, always 0 except in Koikatsu
            /// </summary>
            [Key("CoordinateIndex")]
            public int CoordinateIndex;
            /// <summary>
            /// Slot of the accessory, hair, or clothing
            /// </summary>
            [Key("Slot")]
            public int Slot;
            /// <summary>
            /// Name of the material
            /// </summary>
            [Key("MaterialName")]
            public string MaterialName;
            /// <summary>
            /// Name of the copy
            /// </summary>
            [Key("MaterialCopyName")]
            public string MaterialCopyName;

            public MaterialCopy(ObjectType objectType, int coordinateIndex, int slot, string materialName, string materialCopyName)
            {
                ObjectType = objectType;
                CoordinateIndex = coordinateIndex;
                Slot = slot;
                MaterialName = materialName.FormatShadingObjectName();
                MaterialCopyName = materialCopyName;
            }
        }

        /// <summary>
        /// Data storage class for projector properties
        /// </summary>
        [Serializable]
        [MessagePackObject]
        public class ProjectorProperty
        {
            /// <summary>
            /// Type of the object
            /// </summary>
            [Key("ObjectType")]
            public ObjectType ObjectType;
            /// <summary>
            /// Coordinate index, always 0 except in Koikatsu
            /// </summary>
            [Key("CoordinateIndex")]
            public int CoordinateIndex;
            /// <summary>
            /// Slot of the accessory, hair, or clothing
            /// </summary>
            [Key("Slot")]
            public int Slot;
            /// <summary>
            /// Name of the projector
            /// </summary>
            [Key("ProjectorName")]
            public string ProjectorName;
            /// <summary>
            /// Property type
            /// </summary>
            [Key("Property")]
            public ProjectorProperties Property;
            /// <summary>
            /// Value
            /// </summary>
            [Key("Value")]
            public string Value;
            /// <summary>
            /// Original value
            /// </summary>
            [Key("ValueOriginal")]
            public string ValueOriginal;

            /// <summary>
            /// Data storage class for projector properties
            /// </summary>
            /// <param name="id">ID of the item</param>
            /// <param name="ProjectorName">Name of the projector</param>
            /// <param name="property">Property type</param>
            /// <param name="value">Value</param>
            /// <param name="valueOriginal">Original</param>
            public ProjectorProperty(ObjectType objectType, int coordinateIndex, int slot, string projectorName, ProjectorProperties property, string value, string valueOriginal)
            {
                ObjectType = objectType;
                CoordinateIndex = coordinateIndex;
                Slot = slot;
                ProjectorName = projectorName.FormatShadingObjectName();
                Property = property;
                Value = value;
                ValueOriginal = valueOriginal;
            }
        }
    }
}
