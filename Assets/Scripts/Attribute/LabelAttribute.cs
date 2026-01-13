using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class LabelAttribute : PropertyAttribute
{
    public string label;
    public LabelAttribute(string label)
    {
        this.label = label;
    }
}

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(LabelAttribute))]
public class LabelDrawer : PropertyDrawer
{
    // 1. 높이 계산 (중요: 배열이나 클래스는 높이가 가변적임)
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // 'true' 파라미터는 자식 요소(배열 원소 등)의 높이까지 포함하라는 뜻입니다.
        return EditorGUI.GetPropertyHeight(property, label, true);
    }

    // 2. GUI 그리기
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // Attribute에서 설정한 라벨 가져오기
        var labelAttribute = attribute as LabelAttribute;
        var newLabel = new GUIContent(labelAttribute.label);

        // 'true' 파라미터를 넣어주어야 자식 요소(배열 내부, 클래스 필드 등)가 정상적으로 그려집니다.
        EditorGUI.PropertyField(position, property, newLabel, true);
    }
}
#endif