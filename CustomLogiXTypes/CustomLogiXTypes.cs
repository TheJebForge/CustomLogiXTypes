using BaseX;
using FrooxEngine;
using FrooxEngine.LogiX;
using FrooxEngine.UIX;
using HarmonyLib;
using NeosModLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace CustomLogiXTypes
{
    public class CustomLogiXTypes : NeosMod
    {
        public override string Name => "CustomLogiXTypes";
        public override string Author => "TheJebForge";
        public override string Version => "1.0.0";

        static ModConfiguration _config;

        [AutoRegisterConfigKey]
        readonly static ModConfigurationKey<Dictionary<string, List<string>>> CUSTOM_TYPES_KEY = new ModConfigurationKey<Dictionary<string, List<string>>>(
            "CustomTypes", "Keeps all the custom types you added to LogiX browser");

        static Dictionary<string, List<string>> _customTypes;

        public override void DefineConfiguration(ModConfigurationDefinitionBuilder builder) {
            builder
                .AutoSave(true)
                .Version(new Version(1, 0, 0));
        }

        public override void OnEngineInit() {
            Harmony harmony = new Harmony($"net.{Author}.{Name}");
            harmony.PatchAll();
            _config = GetConfiguration();
            _config?.TryGetValue(CUSTOM_TYPES_KEY, out _customTypes);
            _customTypes = _customTypes ?? new Dictionary<string, List<string>>();
        }

        public static void BuildUI(LogixNodeSelector selector, string path, UIBuilder ui) {
            Button addButton = ui.Button(NeosAssets.Common.Icons.Plus);
            DriveIconColorInstead(addButton, color.Green * 0.6f);
            
            RectTransform addButtonRect = addButton.Icon.Slot.GetComponent<RectTransform>();
            addButtonRect.OffsetMin.Value = new float2(25f, 25f);
            addButtonRect.OffsetMax.Value = new float2(-25f, -25f);
            
            addButton.LocalPressed += (button, data) => ShowComponentAttacher(button, data, path, selector);
            
            InsertTypes(selector, path, ui);
        }

        static void InsertTypes(LogixNodeSelector selector, string path, UIBuilder ui) {
            string fileName = PathUtility.GetFileName(path);
            if (!_customTypes.ContainsKey(fileName)) return;

            MethodInfo method = selector.GetType().GetMethod("OnSelectNodeTypePressed", BindingFlags.NonPublic | BindingFlags.Instance);
            if(method == null) return;

            foreach (string typeStr in _customTypes[fileName]) {
                Type type = TypeHelper.TryResolveAlias(typeStr) ?? WorkerManager.GetType(typeStr);
                
                if(type == null) continue;

                Slot inside = ui.Empty("CustomType");
                ui.NestInto(inside);
                {
                    Type innerType = type.GenericTypeArguments[0];
                    color color = innerType.GetColor();

                    ButtonEventHandler<string> handler = (ButtonEventHandler<string>)method.CreateDelegate(typeof(ButtonEventHandler<string>), selector);
                    Button button = ui.Button(innerType.GetNiceName(), color, handler, typeStr);
                    
                    Image buttonImage = button.Slot.GetComponentInChildren<Image>();
                    buttonImage.Sprite.Target = LogixHelper.GetTypeSprite(selector.World, innerType.GetDimensions(), typeof (Delegate).IsAssignableFrom(innerType));
                    buttonImage.PreserveAspect.Value = false;

                    Button deleteButton = ui.Button(NeosAssets.Common.Icons.Rubbish);
                    DriveIconColorInstead(deleteButton, new color(0.8f, 0f, 0f));
                    
                    RectTransform transform = deleteButton.Slot.GetComponentInChildren<RectTransform>();
                    transform.AnchorMin.Value = new float2(0.8f, 0.8f);
                    transform.AnchorMax.Value = new float2(1f, 1f);
                    
                    deleteButton.LocalPressed += (button1, data) => DeleteType(typeStr, path, selector);
                }
                ui.NestOut();
            }
        }

        static void DriveIconColorInstead(Button button, color color) {
            InteractionElement.ColorDriver driver = button.ColorDrivers[0];
            driver.ColorDrive.Target = button.Icon.Tint;

            driver.NormalColor.Value = color;
            driver.HighlightColor.Value = color * 0.8f;
            driver.PressColor.Value = color * 0.6f;

            button.Slot.GetComponent<Image>().Tint.Value = color.rgb_;
        }

        static void ShowComponentAttacher(IButton button, ButtonEventData eventData, string path, LogixNodeSelector selector) {
            Slot target = button.Slot.AddSlot("Target");

            World world = button.World;

            Slot slot = world.LocalUserSpace.AddSlot("Component Attacher");
            ComponentAttacher componentAttacher = slot.AttachComponent<ComponentAttacher>();

            componentAttacher.TargetSlot.Target = target;
            MethodInfo method = typeof(ComponentAttacher).GetMethod("OpenGenericTypesPressed", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null) method.Invoke(componentAttacher, new object[] { null, null, path });
            
            slot.GlobalPosition = eventData.globalPoint + button.Slot.Forward * -0.05f * world.LocalUser.Root.GlobalScale;
            slot.GlobalRotation = button.Slot.GlobalRotation;
            slot.LocalScale *= world.LocalUser.Root.GlobalScale;

            target.ComponentAdded += component => AddType(path, component.GetType(), selector);
        }

        static void AddType(string path, Type type, LogixNodeSelector selector) {
            string fileName = PathUtility.GetFileName(path);
            
            if (_customTypes.ContainsKey(fileName)) {
                _customTypes[fileName].Insert(0, TypeHelper.TryGetAlias(type) ?? type.FullName);
            }
            else {
                _customTypes.Add(fileName, new List<string>(new[]{TypeHelper.TryGetAlias(type) ?? type.FullName}));
            }

            _config.Set(CUSTOM_TYPES_KEY, _customTypes);
            RefreshSelector(path, selector);
        }

        static void DeleteType(string typeStr, string path, LogixNodeSelector selector) {
            string fileName = PathUtility.GetFileName(path);
            
            if (_customTypes.ContainsKey(fileName)) {
                _customTypes[fileName].Remove(typeStr);
            }

            _config.Set(CUSTOM_TYPES_KEY, _customTypes);
            
            RefreshSelector(path, selector);
        }
        
        static void RefreshSelector(string path, LogixNodeSelector selector) {
            MethodInfo method = typeof(LogixNodeSelector).GetMethod("BuildUI", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null) method.Invoke(selector, new object[] { path, true });
        }

        [HarmonyPatch(typeof(LogixNodeSelector), "BuildUI")]
        class ComponentAttacher_BuildUI_Patch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
                int startIndex = -1;

                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

                for (int i = 0; i < codes.Count; i++) {
                    CodeInstruction instr = codes[i];
                    if (instr.opcode != OpCodes.Call || !((MethodInfo)instr.operand).Name.Contains("GetCommonGenericTypes")) continue;
                    Msg("Found!");
                    startIndex = i - 3;
                    break;
                }

                if (startIndex > -1) {
                    MethodInfo method = typeof(CustomLogiXTypes).GetMethod("BuildUI", BindingFlags.Public | BindingFlags.Static);
                    
                    codes.InsertRange(startIndex, new []
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldarg_1),
                        new CodeInstruction(OpCodes.Ldloc_0),
                        new CodeInstruction(OpCodes.Call, method)
                    });
                    Msg("Patched");
                }

                return codes.AsEnumerable();
            }
        }
    }
}