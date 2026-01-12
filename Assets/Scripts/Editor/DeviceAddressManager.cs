using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

public class DeviceAddressManager : EditorWindow
{
    private class DeviceItem
    {
        public Component component;
        public SerializedObject serializedObj;
        public SerializedProperty property;
        public SerializedProperty propUse;
        public SerializedProperty propDouble;
        public SerializedProperty propAddr;
        public SerializedProperty propDesc;    // 설명 (Description)
        public SerializedProperty propComment; // [New] 코멘트 (Comment - 4줄)
        public SerializedProperty propLabel;
        public SerializedProperty propLocked;
        public string varName;
    }

    private List<DeviceItem> listX = new List<DeviceItem>();
    private List<DeviceItem> listY = new List<DeviceItem>();
    private List<DeviceItem> listM = new List<DeviceItem>();
    private List<DeviceItem> listD = new List<DeviceItem>();
    private List<DeviceItem> listEtc = new List<DeviceItem>();
    private List<DeviceItem> listUnused = new List<DeviceItem>();

    private bool showX = true;
    private bool showY = true;
    private bool showM = true;
    private bool showD = true;
    private bool showEtc = true;
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
        DeviceAddressManager window = GetWindow<DeviceAddressManager>("I/O Mapper");
        window.minSize = new Vector2(700f, 500f);
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
        if (!isDragging)
        {
            ScanScene();
            Repaint();
        }
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
            if (selectedItem != null)
                RestoreSelection(selectedItem.component.GetInstanceID(), selectedItem.property.propertyPath);
        }

        if (GUILayout.Button("Export Comments", EditorStyles.toolbarButton, GUILayout.Width(120)))
        {
            ExportToCommentCSV();
        }
        if (GUILayout.Button("Export Comments(KOR)", EditorStyles.toolbarButton, GUILayout.Width(160)))
        {
            ExportToCommentCSV(true);
        }

        if (GUILayout.Button("Export Labels", EditorStyles.toolbarButton, GUILayout.Width(100)))
        {
            ExportToLabelCSV();
        }
        if (GUILayout.Button("Export Labels(KOR)", EditorStyles.toolbarButton, GUILayout.Width(140)))
        {
            ExportToLabelCSV(true);
        }

        GUILayout.FlexibleSpace();
        GUILayout.Label("Ver 5.6 (Comment Data Fix)", EditorStyles.miniLabel);
        GUILayout.EndHorizontal();
    }

    // -----------------------------------------------------------------------------------
    // 코멘트 내보내기 (PLC 헤더 + 탭 구분자 + 실제 코멘트 데이터 사용)
    // -----------------------------------------------------------------------------------
    private void ExportToCommentCSV(bool isKorean = false)
    {
        
        string path = isKorean ? 
            EditorUtility.SaveFilePanel("Export Comments", "", "COMMENT(KOR).csv", "csv") :        
            EditorUtility.SaveFilePanel("Export Comments", "", "COMMENT.csv", "csv");

        if (string.IsNullOrEmpty(path)) return;

        StringBuilder sb = new StringBuilder();

        // 헤더: "PLC"
        sb.Append("\"Comment Export\"\r\n");
        // 컬럼명
        if(isKorean)
        {
            sb.Append("\"디바이스명\"\t\"코멘트\"\r\n");
        }
        else
        {
            sb.Append("\"Device Name\"\t\"Comment\"\r\n");
        }

        var allLists = new List<List<DeviceItem>> { listX, listY, listM, listD, listEtc };
        foreach (var list in allLists)
        {
            AppendCommentList(sb, list);
        }

        WriteFile(path, sb.ToString());
    }

    private void AppendCommentList(StringBuilder sb, List<DeviceItem> list)
    {
        foreach (var item in list)
        {
            item.serializedObj.Update();
            string addr = item.propAddr.stringValue;
            if (string.IsNullOrEmpty(addr)) continue;

            // 1. 주소 정제 (앞자리 0 제거)
            string cleanAddr = SanitizeAddressForExport(addr);

            // 2. [수정됨] Description이 아니라 실제 Comment 필드 내용을 가져옴
            string rawComment = item.propComment.stringValue;

            // 3. 특수문자 처리 (줄바꿈 -> 공백 치환 포함)
            string safeComment = EscapeCSV(rawComment);
            string safeAddr = EscapeCSV(cleanAddr);

            sb.Append($"\"{safeAddr}\"\t\"{safeComment}\"\r\n");
        }
    }

    // -----------------------------------------------------------------------------------
    // 라벨 내보내기
    // -----------------------------------------------------------------------------------
    private void ExportToLabelCSV(bool isKorean = false)
    {
        string path = isKorean ?
            EditorUtility.SaveFilePanel("Export Global Labels", "", "GlobalLabel(KOR).csv", "csv") :
            EditorUtility.SaveFilePanel("Export Global Labels", "", "GlobalLabel.csv", "csv");

        if (string.IsNullOrEmpty(path)) return;

        StringBuilder sb = new StringBuilder();
        sb.Append("\"Label Export\"\r\n");
        if(isKorean)
        {
            sb.Append("\"클래스\"\t\"라벨명\"\t\"데이터형\"\t\"상수값\"\t\"디바이스\"\t\"코멘트\"\t\"비고\"\t\"시스템 라벨 관련\"\t\"시스템 라벨명\"\t\"속성\"\r\n");
        }
        else
        {
            sb.Append("\"Class\"\t\"Label Name\"\t\"Data Type\"\t\"Constant\"\t\"Device\"\t\"Comment\"\t\"Remark\"\t\"Relation with System Label\"\t\"System Label Name\"\t\"Attribute\"\r\n");
        }

        var allLists = new List<List<DeviceItem>> { listX, listY, listM, listD, listEtc };
        foreach (var list in allLists)
        {
            AppendLabelList(sb, list);
        }

        WriteFile(path, sb.ToString());
    }

    private void AppendLabelList(StringBuilder sb, List<DeviceItem> list)
    {
        foreach (var item in list)
        {
            item.serializedObj.Update();
            string addr = item.propAddr.stringValue;
            string labelName = item.propLabel.stringValue;
            bool isDouble = item.propDouble.boolValue;

            if (string.IsNullOrEmpty(labelName)) continue;

            string cleanAddr = SanitizeAddressForExport(addr);

            string dataType = DetectDataType(cleanAddr, isDouble);            
            string comment = EscapeCSV(item.propComment.stringValue);
            string safeAddr = EscapeCSV(cleanAddr);
            string safeLabel = EscapeCSV(labelName);

            sb.Append($"\"VAR_GLOBAL\"\t\"{safeLabel}\"\t\"{dataType}\"\t\"\"\t\"{safeAddr}\"\t\"{comment}\"\t\"\"\t\"\"\t\"\"\t\"\"\r\n");
        }
    }

    private string SanitizeAddressForExport(string fullAddr)
    {
        if (string.IsNullOrEmpty(fullAddr)) return "";

        var match = Regex.Match(fullAddr, @"^([A-Za-z]+)([0-9A-Fa-f]+)$");
        if (!match.Success) return fullAddr;

        string prefix = match.Groups[1].Value.ToUpper();
        string numStr = match.Groups[2].Value;

        if (prefix == "X" || prefix == "Y")
        {
            try
            {
                int val = System.Convert.ToInt32(numStr, 16);
                return prefix + val.ToString("X");
            }
            catch { return fullAddr; }
        }
        else
        {
            try
            {
                int val = int.Parse(numStr);
                return prefix + val.ToString();
            }
            catch { return fullAddr; }
        }
    }

    private string DetectDataType(string address, bool isDouble)
    {
        if (string.IsNullOrEmpty(address)) return "";
        char prefix = address.ToUpper()[0];

        switch (prefix)
        {
            case 'X': case 'Y': case 'M': return "BOOL";
            case 'D':
            case 'W':
            case 'R':
            case 'U':
                if (isDouble) return "Double Word[Signed]";
                else return "Word[Signed]";
            case 'T': return "TIMER";
            case 'C': return "COUNTER";
            default: return "BOOL";
        }
    }

    private string EscapeCSV(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        // 줄바꿈, 탭, 따옴표 처리
        return input.Replace("\n", " ").Replace("\r", "").Replace("\t", " ").Replace("\"", "\"\"");
    }

    private void WriteFile(string path, string content)
    {
        try
        {
            File.WriteAllText(path, content, Encoding.Unicode);
            EditorUtility.DisplayDialog("Export Success", $"Exported:\n{path}", "OK");
            System.Diagnostics.Process.Start(path);
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Export Failed", $"Error: {ex.Message}", "OK");
        }
    }

    // -----------------------------------------------------------------------------------

    private void DrawSidebar()
    {
        GUILayout.BeginVertical("ProjectBrowserBottomBarBg", GUILayout.Width(SidebarWidth), GUILayout.ExpandHeight(true));

        scrollPos = GUILayout.BeginScrollView(scrollPos);
        {
            EditorGUILayout.Space(5);

            showX = DrawDropZone("Input (X) - Hex", listX, new Color(0.8f, 1f, 1f), "X", showX);
            showY = DrawDropZone("Output (Y) - Hex", listY, new Color(1f, 0.9f, 0.8f), "Y", showY);
            showM = DrawDropZone("Internal (M) - Dec", listM, new Color(0.9f, 1f, 0.8f), "M", showM);
            showD = DrawDropZone("Data (D) - Dec", listD, new Color(0.9f, 0.8f, 1f), "D", showD);
            showEtc = DrawDropZone("Others (Manual)", listEtc, new Color(1f, 0.8f, 1f), "Etc", showEtc);
            showUnused = DrawDropZone("Unused List", listUnused, Color.gray, "Unused", showUnused);

            EditorGUILayout.Space(20);
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private bool DrawDropZone(string title, List<DeviceItem> list, Color headerColor, string zoneType, bool isExpanded)
    {
        Rect headerRect = EditorGUILayout.GetControlRect(GUILayout.Height(25));
        EditorGUI.DrawRect(headerRect, headerColor * 0.5f);

        string arrow = isExpanded ? "▼" : "▶";
        EditorGUI.LabelField(headerRect, $"{arrow} {title} ({list.Count})", EditorStyles.boldLabel);

        if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
        {
            if (Event.current.button == 0)
            {
                isExpanded = !isExpanded;
                Event.current.Use();
            }
        }

        HandleDrop(headerRect, list, -1, zoneType);

        if (isExpanded)
        {
            GUILayout.BeginVertical("box");
            {
                if (list.Count == 0)
                {
                    Rect emptyRect = EditorGUILayout.GetControlRect(GUILayout.Height(30));
                    GUI.Box(emptyRect, "Drag items here", EditorStyles.helpBox);
                    HandleDrop(emptyRect, list, 0, zoneType);
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
                            Rect topHalf = new Rect(itemRect.x, itemRect.y, itemRect.width, itemRect.height / 2);
                            if (topHalf.Contains(Event.current.mousePosition))
                            {
                                EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.y, itemRect.width, 2), Color.cyan);
                                HandleDrop(topHalf, list, i, zoneType);
                            }

                            Rect bottomHalf = new Rect(itemRect.x, itemRect.y + itemRect.height / 2, itemRect.width, itemRect.height / 2);
                            if (bottomHalf.Contains(Event.current.mousePosition))
                            {
                                EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.yMax, itemRect.width, 2), Color.cyan);
                                HandleDrop(bottomHalf, list, i + 1, zoneType);
                            }
                        }
                    }

                    Rect footerRect = EditorGUILayout.GetControlRect(GUILayout.Height(10));
                    if (isDragging && draggingItem != null)
                    {
                        if (footerRect.Contains(Event.current.mousePosition))
                        {
                            EditorGUI.DrawRect(new Rect(footerRect.x, footerRect.y, footerRect.width, 2), Color.cyan);
                            HandleDrop(footerRect, list, list.Count, zoneType);
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
        string labelName = item.propLabel.stringValue;
        bool isLocked = item.propLocked.boolValue;

        GUIStyle bgStyle = new GUIStyle(EditorStyles.objectFieldThumb);
        bool isSelected = (selectedItem != null &&
                           item.component.GetInstanceID() == selectedItem.component.GetInstanceID() &&
                           item.property.propertyPath == selectedItem.property.propertyPath);

        if (isSelected)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(0f, 0.5f, 1f, 0.3f));
            tex.Apply();
            bgStyle.normal.background = tex;
        }

        Rect rect = EditorGUILayout.GetControlRect(GUILayout.Height(24));

        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            if (Event.current.button == 0)
            {
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

        GUI.Box(rect, GUIContent.none, bgStyle);

        string lockMark = isLocked ? "🔒" : "";
        string leftText = "";

        if (currentList == listUnused)
        {
            leftText = item.varName;
        }
        else
        {
            if (string.IsNullOrEmpty(addr)) leftText = $"{lockMark} (No Addr)";
            else leftText = string.IsNullOrEmpty(labelName) ? $"{lockMark}[{addr}]" : $"{lockMark}[{addr}] {labelName}";
        }

        string rightText = string.IsNullOrEmpty(desc) ? "" : $"({desc})";

        GUIStyle leftStyle = new GUIStyle(EditorStyles.label);
        leftStyle.alignment = TextAnchor.MiddleLeft;
        leftStyle.wordWrap = false;
        leftStyle.clipping = TextClipping.Clip;
        if (isSelected) leftStyle.normal.textColor = Color.cyan;
        else leftStyle.normal.textColor = isLocked ? new Color(1f, 0.5f, 0.5f) : GUI.skin.label.normal.textColor;

        GUIStyle rightStyle = new GUIStyle(EditorStyles.miniLabel);
        rightStyle.alignment = TextAnchor.MiddleLeft;
        rightStyle.wordWrap = false;
        rightStyle.clipping = TextClipping.Clip;
        rightStyle.normal.textColor = Color.gray;

        float padding = 4f;
        float leftRatio = 0.55f;

        Rect leftRect = new Rect(rect.x + padding, rect.y, (rect.width * leftRatio) - padding, rect.height);
        Rect rightRect = new Rect(rect.x + (rect.width * leftRatio), rect.y, (rect.width * (1 - leftRatio)) - padding, rect.height);

        GUI.Label(leftRect, leftText, leftStyle);
        GUI.Label(rightRect, rightText, rightStyle);

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

            string uniqueControlId = "InspectorField_" + selectedItem.component.GetInstanceID() + selectedItem.property.propertyPath;
            GUI.SetNextControlName(uniqueControlId);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(selectedItem.property, true);
            if (EditorGUI.EndChangeCheck())
            {
                selectedItem.serializedObj.ApplyModifiedProperties();

                int currentID = selectedItem.component.GetInstanceID();
                string currentPath = selectedItem.property.propertyPath;

                ScanScene();
                RestoreSelection(currentID, currentPath);
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

    private void RestoreSelection(int instanceID, string propertyPath)
    {
        var allLists = new List<List<DeviceItem>> { listX, listY, listM, listD, listEtc, listUnused };
        foreach (var list in allLists)
        {
            var found = list.FirstOrDefault(x => x.component.GetInstanceID() == instanceID && x.property.propertyPath == propertyPath);
            if (found != null)
            {
                selectedItem = found;
                return;
            }
        }
        selectedItem = null;
    }

    private void HandleDrop(Rect dropArea, List<DeviceItem> targetList, int insertIndex, string zoneType)
    {
        Event e = Event.current;

        if (isDragging && draggingItem != null && dropArea.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseUp)
            {
                int oldIndex = -1;
                if (sourceList != null)
                {
                    oldIndex = sourceList.IndexOf(draggingItem);
                    sourceList.Remove(draggingItem);
                }

                if (sourceList == targetList && oldIndex != -1)
                {
                    if (oldIndex < insertIndex) insertIndex--;
                }

                if (insertIndex < 0) insertIndex = 0;
                if (insertIndex > targetList.Count) insertIndex = targetList.Count;

                targetList.Insert(insertIndex, draggingItem);

                UpdateItemProperties(draggingItem, zoneType);
                AutoReaddressList(targetList, zoneType);

                isDragging = false;
                draggingItem = null;
                sourceList = null;

                e.Use();
                GUIUtility.ExitGUI();
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
            item.propLabel.stringValue = "";
            item.propLocked.boolValue = false;
            item.propDouble.boolValue = false;
        }
        else
        {
            item.propUse.boolValue = true;
        }
        item.serializedObj.ApplyModifiedProperties();
    }

    private void AutoReaddressList(List<DeviceItem> list, string zonePrefix)
    {
        if (zonePrefix == "Unused" || zonePrefix == "Etc") return;

        int currentCounter = 0;

        for (int i = 0; i < list.Count; i++)
        {
            DeviceItem item = list[i];
            item.serializedObj.Update();

            bool isLocked = item.propLocked.boolValue;

            if (isLocked)
            {
                string myAddr = item.propAddr.stringValue;
                int myNum = ParseAddressNumber(myAddr);
                if (myNum >= 0)
                {
                    currentCounter = myNum + 1;
                }
            }
            else
            {
                string newAddr = "";
                if (zonePrefix == "X" || zonePrefix == "Y")
                    newAddr = zonePrefix + currentCounter.ToString("X2");
                else
                    newAddr = zonePrefix + currentCounter.ToString();

                item.propAddr.stringValue = newAddr;
                currentCounter++;
            }

            item.serializedObj.ApplyModifiedProperties();
        }
    }

    private int ParseAddressNumber(string addr)
    {
        if (string.IsNullOrEmpty(addr)) return -1;
        var match = Regex.Match(addr, @"\d+");
        if (!match.Success)
        {
            string sub = addr.Substring(1);
            try { return System.Convert.ToInt32(sub, 16); } catch { return -1; }
        }

        char prefix = addr.ToUpper()[0];
        string numberStr = match.Value;

        if (prefix == 'X' || prefix == 'Y')
        {
            string sub = addr.Substring(1);
            try { return System.Convert.ToInt32(sub, 16); } catch { return -1; }
        }
        else
        {
            try { return int.Parse(numberStr); } catch { return -1; }
        }
    }

    private void ScanScene()
    {
        List<DeviceItem> allItems = new List<DeviceItem>();

        MXObject[] scripts = FindObjectsByType<MXObject>(FindObjectsSortMode.None);

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
                        propDouble = prop.FindPropertyRelative("useDoubleWord"),
                        propAddr = prop.FindPropertyRelative("address"),
                        propDesc = prop.FindPropertyRelative("description"),
                        propComment = prop.FindPropertyRelative("comment"), // [New] Scan Logic
                        propLabel = prop.FindPropertyRelative("label"),
                        propLocked = prop.FindPropertyRelative("isLocked"),
                        varName = iter.displayName
                    });
                }
            }
        }

        listX.Clear(); listY.Clear(); listM.Clear(); listD.Clear(); listEtc.Clear(); listUnused.Clear();

        foreach (var item in allItems)
        {
            item.serializedObj.Update();
            if (!item.propUse.boolValue)
            {
                listUnused.Add(item);
            }
            else
            {
                string addr = item.propAddr.stringValue.ToUpper().Trim();

                if (addr.StartsWith("X")) listX.Add(item);
                else if (addr.StartsWith("Y")) listY.Add(item);
                else if (addr.StartsWith("M")) listM.Add(item);
                else if (addr.StartsWith("D")) listD.Add(item);
                else listEtc.Add(item);
            }
        }

        SortListByAddress(listX);
        SortListByAddress(listY);
        SortListByAddress(listM);
        SortListByAddress(listD);
        SortListByAddress(listEtc);
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