using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace AshleyR
{
    public class DeviceAddressManager : EditorWindow
    {
        private class DeviceItem
        {
            public Component component;
            public SerializedObject serializedObj;
            public SerializedProperty property;
            public SerializedProperty propUse;
            public SerializedProperty propAddr;
            public SerializedProperty propDesc;
            public string varName;
        }

        // 5개의 리스트 (D 추가됨)
        private List<DeviceItem> listX = new List<DeviceItem>();
        private List<DeviceItem> listY = new List<DeviceItem>();
        private List<DeviceItem> listM = new List<DeviceItem>(); // Buffer
        private List<DeviceItem> listD = new List<DeviceItem>(); // Data Register [New]
        private List<DeviceItem> listUnused = new List<DeviceItem>();

        // 접기/펼치기 상태 변수
        private bool showX = true;
        private bool showY = true;
        private bool showM = true;
        private bool showD = true;
        private bool showUnused = true;

        private DeviceItem selectedItem = null;
        private DeviceItem draggingItem = null;
        private List<DeviceItem> sourceList = null;
        private bool isDragging = false;

        private Vector2 scrollPos;
        private const float SidebarWidth = 330f;

        [MenuItem("Factory Tools/Device Address Manager")]
        public static void ShowWindow()
        {
            GetWindow<DeviceAddressManager>("I/O Mapper");
        }

        private void OnEnable()
        {
            ScanScene();
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        private void OnHierarchyChanged()
        {
            ScanScene();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            GUILayout.BeginHorizontal();
            {
                DrawSidebar();
                DrawVerticalLine();
                DrawInspector();
            }
            GUILayout.EndHorizontal();

            // 드래그 중 라벨 표시
            if (isDragging && draggingItem != null)
            {
                GUI.Label(new Rect(Event.current.mousePosition.x + 15, Event.current.mousePosition.y, 200, 30),
                    $"Move: {draggingItem.propDesc.stringValue}", EditorStyles.whiteLabel);
                Repaint();
            }

            if (Event.current.type == EventType.MouseUp && isDragging)
            {
                CancelDrag();
            }
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh Scan", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                ScanScene();
            }

            if (GUILayout.Button("Export CSV", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                ExportToCSV();
            }

            GUILayout.FlexibleSpace();
            // [업데이트 확인용 라벨]
            GUILayout.Label("Ver 2.0 (Foldable)", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }

        private void ExportToCSV()
        {
            string path = EditorUtility.SaveFilePanel("Export Device Address", "", "COMMENT.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("\"EX01\"");
            sb.AppendLine("\"디바이스명\"\t\"코멘트\"");

            AppendListToCSV(sb, listX);
            AppendListToCSV(sb, listY);
            AppendListToCSV(sb, listM);
            AppendListToCSV(sb, listD); // D 리스트 내보내기

            try
            {
                File.WriteAllText(path, sb.ToString(), Encoding.Unicode);
                EditorUtility.DisplayDialog("Export Success", $"Exported for GX Works2:\n{path}", "OK");
                System.Diagnostics.Process.Start(path);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Export Failed", $"Error: {ex.Message}", "OK");
            }
        }

        private void AppendListToCSV(StringBuilder sb, List<DeviceItem> list)
        {
            foreach (var item in list)
            {
                item.serializedObj.Update();
                string addr = item.propAddr.stringValue;
                if (string.IsNullOrEmpty(addr)) continue;

                string desc = item.propDesc.stringValue;
                string safeDesc = desc.Replace("\n", " ").Replace("\r", "").Replace("\"", "\"\"");
                string safeAddr = addr.Replace("\"", "\"\"");

                sb.AppendLine($"\"{safeAddr}\"\t\"{safeDesc}\"");
            }
        }

        private void DrawSidebar()
        {
            GUILayout.BeginVertical("ProjectBrowserBottomBarBg", GUILayout.Width(SidebarWidth), GUILayout.ExpandHeight(true));

            scrollPos = GUILayout.BeginScrollView(scrollPos);
            {
                EditorGUILayout.Space(5);

                // 각 구역 그리기 (접기 상태 반영)
                showX = DrawDropZone("Input (X) - Hex", listX, new Color(0.8f, 1f, 1f), "X", showX);
                showY = DrawDropZone("Output (Y) - Hex", listY, new Color(1f, 0.9f, 0.8f), "Y", showY);
                showM = DrawDropZone("Buffer (M) - Dec", listM, new Color(0.9f, 1f, 0.8f), "M", showM);
                showD = DrawDropZone("Data (D) - Dec", listD, new Color(0.9f, 0.8f, 1f), "D", showD); // [New]
                showUnused = DrawDropZone("Unused List", listUnused, Color.gray, "Unused", showUnused);

                EditorGUILayout.Space(20);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private bool DrawDropZone(string title, List<DeviceItem> list, Color headerColor, string zoneType, bool isExpanded)
        {
            // 1. 헤더 그리기
            Rect headerRect = EditorGUILayout.GetControlRect(GUILayout.Height(25));
            EditorGUI.DrawRect(headerRect, headerColor * 0.5f);

            // 화살표 및 제목
            string arrow = isExpanded ? "▼" : "▶";
            EditorGUI.LabelField(headerRect, $"{arrow} {title} ({list.Count})", EditorStyles.boldLabel);

            // 클릭 시 접기/펼치기
            if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 0)
                {
                    isExpanded = !isExpanded;
                    Event.current.Use();
                }
            }

            // 헤더 드롭 처리 (접혀있어도 드롭 가능하게)
            HandleDrop(headerRect, list, -1, zoneType);

            // 2. 리스트 내용 (펼쳐졌을 때만 그림)
            if (isExpanded)
            {
                GUILayout.BeginVertical("box");
                {
                    if (list.Count == 0)
                    {
                        Rect emptyRect = EditorGUILayout.GetControlRect(GUILayout.Height(30));
                        GUI.Box(emptyRect, "Drag items here", EditorStyles.helpBox);
                        HandleDrop(emptyRect, list, -1, zoneType);
                    }
                    else
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            DeviceItem item = list[i];
                            if (item.component == null) continue;

                            Rect itemRect = DrawItem(item, list);

                            if (isDragging && draggingItem != null && item != draggingItem)
                            {
                                if (itemRect.Contains(Event.current.mousePosition))
                                {
                                    Rect lineRect = new Rect(itemRect.x, itemRect.y, itemRect.width, 2);
                                    EditorGUI.DrawRect(lineRect, Color.cyan);
                                    HandleDrop(itemRect, list, i, zoneType);
                                }
                            }
                        }
                    }
                }
                GUILayout.EndVertical();
            }

            EditorGUILayout.Space(5);
            return isExpanded;
        }

        private Rect DrawItem(DeviceItem item, List<DeviceItem> currentList)
        {
            item.serializedObj.Update();

            string addr = item.propAddr.stringValue;
            string desc = item.propDesc.stringValue.Replace("\n", " ");
            string label = string.IsNullOrEmpty(addr) ? desc : $"[{addr}] {desc}";
            if (currentList == listUnused) label = item.varName;

            GUIStyle style = new GUIStyle(EditorStyles.objectFieldThumb);
            if (selectedItem == item)
            {
                style.normal.textColor = Color.cyan;
                style.fontStyle = FontStyle.Bold;
            }

            Rect rect = EditorGUILayout.GetControlRect(GUILayout.Height(24));

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 0)
                {
                    // [포커스 해제]
                    GUI.FocusControl(null);
                    EditorGUIUtility.editingTextField = false;

                    selectedItem = item;
                    EditorGUIUtility.PingObject(item.component.gameObject);

                    draggingItem = item;
                    sourceList = currentList;
                    isDragging = true;
                    Event.current.Use();
                    Repaint();
                }
            }

            GUI.Box(rect, new GUIContent(label, item.component.gameObject.name), style);
            return rect;
        }

        private void DrawInspector()
        {
            GUILayout.BeginVertical(GUILayout.ExpandHeight(true));

            if (selectedItem != null && selectedItem.component != null)
            {
                GUILayout.Space(10);
                GUILayout.Label($"Editing: {selectedItem.component.gameObject.name}", EditorStyles.boldLabel);
                GUILayout.Space(10);

                selectedItem.serializedObj.Update();

                string uniqueControlId = "InspectorField_" + selectedItem.component.GetInstanceID();
                GUI.SetNextControlName(uniqueControlId);

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(selectedItem.property, true);
                if (EditorGUI.EndChangeCheck())
                {
                    selectedItem.serializedObj.ApplyModifiedProperties();
                    Repaint();
                }
            }
            else
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Select or Drag an item", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndVertical();
        }

        private void HandleDrop(Rect dropArea, List<DeviceItem> targetList, int insertIndex, string zoneType)
        {
            Event e = Event.current;

            if (isDragging && draggingItem != null && dropArea.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseUp)
                {
                    if (sourceList != null) sourceList.Remove(draggingItem);

                    if (insertIndex >= 0 && insertIndex <= targetList.Count)
                    {
                        targetList.Insert(insertIndex, draggingItem);
                    }
                    else
                    {
                        targetList.Add(draggingItem);
                    }

                    UpdateItemProperties(draggingItem, zoneType);
                    AutoReaddressList(targetList, zoneType);

                    isDragging = false;
                    draggingItem = null;
                    sourceList = null;
                    e.Use();
                    Repaint();
                }
            }
        }

        private void CancelDrag()
        {
            isDragging = false;
            draggingItem = null;
            sourceList = null;
            Repaint();
        }

        private void UpdateItemProperties(DeviceItem item, string targetZone)
        {
            item.serializedObj.Update();
            if (targetZone == "Unused")
            {
                item.propUse.boolValue = false;
                item.propAddr.stringValue = "";
            }
            else
            {
                item.propUse.boolValue = true;
            }
            item.serializedObj.ApplyModifiedProperties();
        }

        private void AutoReaddressList(List<DeviceItem> list, string zonePrefix)
        {
            if (zonePrefix == "Unused") return;

            for (int i = 0; i < list.Count; i++)
            {
                DeviceItem item = list[i];
                item.serializedObj.Update();

                string newAddr = "";

                // X, Y는 16진수, 그 외(M, D)는 10진수
                if (zonePrefix == "X" || zonePrefix == "Y")
                {
                    newAddr = zonePrefix + i.ToString("X2");
                }
                else
                {
                    newAddr = zonePrefix + i.ToString();
                }

                item.propAddr.stringValue = newAddr;
                item.serializedObj.ApplyModifiedProperties();
            }
        }

        private void ScanScene()
        {
            List<DeviceItem> allItems = new List<DeviceItem>();
            MonoBehaviour[] scripts = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            foreach (var script in scripts)
            {
                if (script == null) continue;
                SerializedObject so = new SerializedObject(script);
                SerializedProperty iter = so.GetIterator();
                bool enterChildren = true;

                while (iter.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (iter.type == nameof(DeviceAddress))
                    {
                        SerializedProperty prop = iter.Copy();
                        allItems.Add(new DeviceItem()
                        {
                            component = script,
                            serializedObj = so,
                            property = prop,
                            propUse = prop.FindPropertyRelative("useDevice"),
                            propAddr = prop.FindPropertyRelative("address"),
                            propDesc = prop.FindPropertyRelative("description"),
                            varName = iter.displayName
                        });
                    }
                }
            }

            // 리스트 초기화
            listX.Clear(); listY.Clear(); listM.Clear(); listD.Clear(); listUnused.Clear();

            // 분류 로직 (D 추가됨)
            foreach (var item in allItems)
            {
                item.serializedObj.Update();
                if (!item.propUse.boolValue)
                {
                    listUnused.Add(item);
                }
                else
                {
                    string addr = item.propAddr.stringValue.ToUpper();
                    if (addr.StartsWith("X")) listX.Add(item);
                    else if (addr.StartsWith("Y")) listY.Add(item);
                    else if (addr.StartsWith("M")) listM.Add(item);
                    else if (addr.StartsWith("D")) listD.Add(item);
                    else listM.Add(item); // 기본값 M
                }
            }

            SortListByAddress(listX);
            SortListByAddress(listY);
            SortListByAddress(listM);
            SortListByAddress(listD);
        }

        private void SortListByAddress(List<DeviceItem> list)
        {
            list.Sort((a, b) => string.Compare(a.propAddr.stringValue, b.propAddr.stringValue));
        }

        private void DrawVerticalLine()
        {
            GUILayout.Box(GUIContent.none, GUILayout.Width(1), GUILayout.ExpandHeight(true));
            Rect r = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(r, new Color(0.1f, 0.1f, 0.1f, 1));
        }
    }
}
