using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(DeviceAddress))]
public class DeviceAddressDrawer : PropertyDrawer
{
    private const float HeaderHeight = 20f;
    private const float LineHeight = 18f;
    private const float Spacing = 2f;
    private const float Padding = 6f;

    private const int CommentLineCount = 4;
    private const int MaxBytesPerLine = 8;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float bodyHeight = (LineHeight + Spacing) * CommentLineCount;
        return HeaderHeight + bodyHeight + (Padding * 2);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        GUI.Box(position, GUIContent.none, "HelpBox");

        Rect contentRect = new Rect(
            position.x + Padding,
            position.y + Padding,
            position.width - (Padding * 2),
            position.height - (Padding * 2)
        );

        var useDevice = property.FindPropertyRelative("useDevice");
        var description = property.FindPropertyRelative("description");
        var address = property.FindPropertyRelative("address");
        var comment = property.FindPropertyRelative("comment");

        // --- 1. 헤더 영역 ---
        Rect headerRect = new Rect(contentRect.x, contentRect.y, contentRect.width, HeaderHeight);

        string headerLabel = "Device Definition";
        if (useDevice.boolValue && !string.IsNullOrEmpty(address.stringValue))
        {
            headerLabel += " - " + address.stringValue;
        }

        EditorGUI.LabelField(new Rect(headerRect.x, headerRect.y, headerRect.width - 20, headerRect.height), headerLabel, EditorStyles.boldLabel);

        // [수정됨] 체크박스 변경 감지 로직 추가
        Rect toggleRect = new Rect(headerRect.x + headerRect.width - 20, headerRect.y, 20, headerRect.height);

        EditorGUI.BeginChangeCheck(); // 변경 감지 시작
        EditorGUI.PropertyField(toggleRect, useDevice, GUIContent.none);
        if (EditorGUI.EndChangeCheck()) // 값이 바뀌었다면?
        {
            // 체크가 해제되었다면(false), 주소(address) 내용을 비워버림
            if (!useDevice.boolValue)
            {
                address.stringValue = "";
            }
        }


        // --- 2. 본문 활성 처리 ---
        bool originalEnabled = GUI.enabled;
        GUI.enabled = useDevice.boolValue;

        float bodyStartY = contentRect.y + HeaderHeight + Spacing;

        // 레이아웃 치수 계산
        float leftWidth = contentRect.width * 0.60f;
        float rightWidth = contentRect.width * 0.40f - Spacing;
        float labelWidth = 45f;
        float fieldWidth = leftWidth - labelWidth - 4f;

        // -----------------------------------------------------------------------
        // [위치 계산]
        // -----------------------------------------------------------------------

        // Description 위치 (3줄)
        float descHeight = (LineHeight * 3) + (Spacing * 2);
        Rect descLabelRect = new Rect(contentRect.x, bodyStartY, labelWidth, LineHeight);
        Rect descFieldRect = new Rect(contentRect.x + labelWidth, bodyStartY, fieldWidth, descHeight);

        // Address 위치 (맨 아래 4번째 줄)
        float addrY = bodyStartY + (3 * (LineHeight + Spacing));
        Rect addrLabelRect = new Rect(contentRect.x, addrY, labelWidth, LineHeight);
        Rect addrFieldRect = new Rect(contentRect.x + labelWidth, addrY, fieldWidth, LineHeight);

        // Comment 위치들
        Rect[] commentRects = new Rect[CommentLineCount];
        for (int i = 0; i < CommentLineCount; i++)
        {
            float currentY = bodyStartY + (i * (LineHeight + Spacing));
            commentRects[i] = new Rect(contentRect.x + leftWidth + Spacing, currentY, rightWidth, LineHeight);
        }

        // -----------------------------------------------------------------------
        // [그리기 단계] 탭 순서: Desc -> Addr -> Comment
        // -----------------------------------------------------------------------

        // 1. Description
        EditorGUI.LabelField(descLabelRect, "Desc", EditorStyles.miniLabel);
        description.stringValue = EditorGUI.TextArea(descFieldRect, description.stringValue);

        // 2. Address (대문자 자동 변환)
        EditorGUI.LabelField(addrLabelRect, "Addr", EditorStyles.miniLabel);

        EditorGUI.BeginChangeCheck();
        string newAddr = EditorGUI.TextField(addrFieldRect, address.stringValue);
        if (EditorGUI.EndChangeCheck())
        {
            address.stringValue = newAddr.ToUpper();
        }

        // 3. Comments
        string[] lines = comment.stringValue.Split('\n');
        if (lines.Length != CommentLineCount)
        {
            string[] newLines = new string[CommentLineCount];
            for (int i = 0; i < CommentLineCount; i++) newLines[i] = (i < lines.Length) ? lines[i] : "";
            lines = newLines;
        }

        bool isChanged = false;

        for (int i = 0; i < CommentLineCount; i++)
        {
            string oldVal = lines[i];
            string newVal = EditorGUI.TextField(commentRects[i], oldVal);

            if (oldVal != newVal)
            {
                lines[i] = TruncateStringByByte(newVal, MaxBytesPerLine);
                isChanged = true;
            }
        }

        if (isChanged)
        {
            comment.stringValue = string.Join("\n", lines);
        }

        GUI.enabled = originalEnabled;
        EditorGUI.EndProperty();
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