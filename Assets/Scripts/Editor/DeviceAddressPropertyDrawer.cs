using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(DeviceAddress))]
public class DeviceAddressDrawer : PropertyDrawer
{
    private const float HeaderHeight = 22f;
    private const float SingleLine = 18f;
    private const float Spacing = 2f;
    private const float Padding = 6f;

    // 코멘트 제한 설정
    private const int CommentLineCount = 4;
    private const int MaxBytesPerLine = 8;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float bodyHeight = (SingleLine + Spacing) * 4;
        return HeaderHeight + bodyHeight + (Padding * 2);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // 스타일 정의
        GUIStyle noWrapMini = new GUIStyle(EditorStyles.miniLabel);
        noWrapMini.wordWrap = false;
        noWrapMini.clipping = TextClipping.Clip;

        EditorGUI.BeginProperty(position, label, property);

        // 배경 박스
        GUI.Box(position, GUIContent.none, "HelpBox");

        // 패딩 적용된 내부 영역
        Rect contentRect = new Rect(
            position.x + Padding,
            position.y + Padding,
            position.width - (Padding * 2),
            position.height - (Padding * 2)
        );

        var useDevice = property.FindPropertyRelative("useDevice");
        var useDoubleWord = property.FindPropertyRelative("useDoubleWord");
        var isLocked = property.FindPropertyRelative("isLocked");
        var labelProp = property.FindPropertyRelative("label");
        var address = property.FindPropertyRelative("address");
        var description = property.FindPropertyRelative("description");
        var comment = property.FindPropertyRelative("comment");

        // ---------------------------------------------------------------------
        // 1. 헤더 (Header)
        // ---------------------------------------------------------------------
        Rect headerRect = new Rect(contentRect.x, contentRect.y, contentRect.width, HeaderHeight);

        string headerText = "Device Definition";
        if (useDevice.boolValue)
        {
            string l = labelProp.stringValue;
            string a = address.stringValue;
            if (!string.IsNullOrEmpty(l)) headerText += $" [{l}]";
            else if (!string.IsNullOrEmpty(a)) headerText += $" - {a}";
        }

        EditorGUI.LabelField(new Rect(headerRect.x, headerRect.y, headerRect.width - 20, headerRect.height), headerText, EditorStyles.boldLabel);

        // Toggle (오른쪽 상단)
        Rect toggleRect = new Rect(headerRect.x + headerRect.width - 20, headerRect.y, 20, headerRect.height);

        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(toggleRect, useDevice, GUIContent.none);
        if (EditorGUI.EndChangeCheck())
        {
            if (!useDevice.boolValue)
            {
                address.stringValue = "";
                labelProp.stringValue = "";
                isLocked.boolValue = false;
                useDoubleWord.boolValue = false;
            }
        }

        bool originalEnabled = GUI.enabled;
        GUI.enabled = useDevice.boolValue;

        // ---------------------------------------------------------------------
        // 2. 본문 레이아웃 (좌우 2단 분할)
        // ---------------------------------------------------------------------
        float startY = contentRect.y + HeaderHeight + Spacing;

        float leftWidth = contentRect.width * 0.65f;
        float rightWidth = contentRect.width * 0.35f - 4f;

        float labelWidth = 40f;
        float fieldWidth = leftWidth - labelWidth - 4f;

        // --- [왼쪽 컬럼] ---
        // (1) Description (2줄)
        Rect descLblRect = new Rect(contentRect.x, startY, labelWidth, SingleLine);
        Rect descFldRect = new Rect(contentRect.x + labelWidth, startY, fieldWidth, (SingleLine * 2) + Spacing);

        EditorGUI.LabelField(descLblRect, "Desc", noWrapMini);
        description.stringValue = EditorGUI.TextArea(descFldRect, description.stringValue);

        // (2) Label (1줄)
        float row3Y = startY + (SingleLine * 2) + (Spacing * 2);
        Rect labelLblRect = new Rect(contentRect.x, row3Y, labelWidth, SingleLine);
        Rect labelFldRect = new Rect(contentRect.x + labelWidth, row3Y, fieldWidth, SingleLine);

        EditorGUI.LabelField(labelLblRect, "Label", noWrapMini);
        labelProp.stringValue = EditorGUI.TextField(labelFldRect, labelProp.stringValue);

        // (3) Address (1줄) + [DW 체크] + [Lock]
        float row4Y = row3Y + SingleLine + Spacing;
        Rect addrLblRect = new Rect(contentRect.x, row4Y, labelWidth, SingleLine);

        // 버튼 사이즈 정의
        float lockBtnW = 22f;
        float dwBtnW = 35f; // [수정] 28f -> 35f로 변경 (글자 잘림 해결)

        // 주소가 D 또는 U로 시작하는지 확인
        bool showDoubleWordOption = false;
        string currentAddr = address.stringValue.ToUpper().Trim();
        if (!string.IsNullOrEmpty(currentAddr))
        {
            char firstChar = currentAddr[0];
            if (firstChar == 'D' || firstChar == 'U')
            {
                showDoubleWordOption = true;
            }
        }

        // 만약 조건이 안 맞으면 강제로 false 처리
        if (!showDoubleWordOption && useDoubleWord.boolValue)
        {
            useDoubleWord.boolValue = false;
        }

        // 입력 필드 너비 계산 (DW 버튼 유무에 따라)
        float currentFieldWidth = fieldWidth - lockBtnW - 2;
        if (showDoubleWordOption) currentFieldWidth -= (dwBtnW + 2);

        Rect addrFldRect = new Rect(contentRect.x + labelWidth, row4Y, currentFieldWidth, SingleLine);

        // DW 버튼 위치
        Rect dwBtnRect = new Rect(addrFldRect.x + addrFldRect.width + 2, row4Y, dwBtnW, SingleLine);

        // Lock 버튼 위치
        float lockBtnX = showDoubleWordOption ? (dwBtnRect.x + dwBtnRect.width + 2) : (addrFldRect.x + addrFldRect.width + 2);
        Rect lockBtnRect = new Rect(lockBtnX, row4Y, lockBtnW, SingleLine);

        EditorGUI.LabelField(addrLblRect, "Addr", noWrapMini);

        // [DW 버튼 그리기]
        if (showDoubleWordOption)
        {
            bool dwState = useDoubleWord.boolValue;
            GUIStyle dwStyle = new GUIStyle(EditorStyles.miniButton);
            if (dwState) dwStyle.fontStyle = FontStyle.Bold;

            Color backupColor = GUI.backgroundColor;
            if (dwState) GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);

            if (GUI.Button(dwBtnRect, new GUIContent("DW", "Double Word (32bit)"), dwStyle))
            {
                useDoubleWord.boolValue = !useDoubleWord.boolValue;
            }
            GUI.backgroundColor = backupColor;
        }

        // [Lock 버튼 그리기]
        string lockIcon = isLocked.boolValue ? "🔒" : "🔓";
        if (GUI.Button(lockBtnRect, new GUIContent(lockIcon, "Lock Address")))
        {
            isLocked.boolValue = !isLocked.boolValue;
        }

        // [주소 입력]
        bool wasEnabled = GUI.enabled;
        if (isLocked.boolValue) GUI.enabled = false;

        EditorGUI.BeginChangeCheck();
        string newAddr = EditorGUI.DelayedTextField(addrFldRect, address.stringValue);
        if (EditorGUI.EndChangeCheck())
        {
            address.stringValue = FormatDeviceAddress(newAddr);
            if (!string.IsNullOrEmpty(address.stringValue)) isLocked.boolValue = true;
        }
        GUI.enabled = wasEnabled;

        // --- [오른쪽 컬럼] 코멘트 4줄 ---
        string[] lines = comment.stringValue.Split('\n');
        if (lines.Length != CommentLineCount)
        {
            string[] newLines = new string[CommentLineCount];
            for (int i = 0; i < CommentLineCount; i++)
                newLines[i] = (i < lines.Length) ? lines[i] : "";
            lines = newLines;
        }

        bool isCommentChanged = false;
        float commentY = startY;

        for (int i = 0; i < CommentLineCount; i++)
        {
            Rect commentRect = new Rect(contentRect.x + leftWidth + 4f, commentY, rightWidth, SingleLine);
            string oldVal = lines[i];
            string newVal = EditorGUI.TextField(commentRect, oldVal);

            if (oldVal != newVal)
            {
                lines[i] = TruncateStringByByte(newVal, MaxBytesPerLine);
                isCommentChanged = true;
            }
            commentY += SingleLine + Spacing;
        }

        if (isCommentChanged)
        {
            comment.stringValue = string.Join("\n", lines);
        }

        GUI.enabled = originalEnabled;
        EditorGUI.EndProperty();
    }

    private string FormatDeviceAddress(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        input = input.ToUpper().Trim();
        if (input.Length < 2) return input;

        char type = input[0];
        string numStr = input.Substring(1);

        if (type == 'X' || type == 'Y')
        {
            if (int.TryParse(numStr, System.Globalization.NumberStyles.HexNumber, null, out int val))
                return type + val.ToString("X2");
        }
        else if (type == 'M' || type == 'D' || type == 'T' || type == 'C')
        {
            if (int.TryParse(numStr, out int val))
                return type + val.ToString();
        }
        return input;
    }

    private string TruncateStringByByte(string input, int maxBytes)
    {
        int currentBytes = 0;
        string result = "";
        foreach (char c in input)
        {
            int charByteSize = (c <= 0x7F) ? 1 : 2;
            if (currentBytes + charByteSize > maxBytes) break;
            currentBytes += charByteSize;
            result += c;
        }
        return result;
    }
}