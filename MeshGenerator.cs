using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

public class MeshGenerator : EditorWindow
{
    enum VerticesType
    {
        PhsyicsShape,
        OutLine,
    }

    private const string KEY_SAVE_PATH = "MeshGenSavePath";
    private const string KEY_THICKNESS = "MeshThickness";
    private const float THICKNESS_MIN = 0f;
    private const float THICKNESS_MAX = 10f;

    private const float DEFAULT_BUTTON_WIDTH = 100f;
    private const float DEFAULT_BUTTON_HEIGHT = 20f;
    private const float BUTTON_SPACE = 5f;

    private Sprite targetSprite = null;
    private VerticesType verticesType = VerticesType.PhsyicsShape;

    [MenuItem("Custom/Mesh/Mesh generator")]
    public static void ShowWindow()
    {
        //Open window
        MeshGenerator window = GetWindow<MeshGenerator>("Mesh generator");
        window.minSize = new Vector2(350, 200);
    }

    private void OnGUI()
    {
        DrawPathGUI();
        DrawMeshInfoGUI();
        DrawSaveAssetGUI();
    }

    private void DrawPathGUI()
    {
        GUILayout.Space(10);
        GUILayout.Label("Path", EditorStyles.boldLabel);

        GUILayout.BeginHorizontal();
        string targetSavePath = GetTargetSavePath();
        EditorGUILayout.TextField("Target save path", targetSavePath);
        if (GUILayout.Button("Browser", GUILayout.Width(DEFAULT_BUTTON_WIDTH), GUILayout.Height(DEFAULT_BUTTON_HEIGHT)))
        {
            targetSavePath = EditorUtility.OpenFolderPanel("Select Folder", string.Empty, string.Empty);
            SetTargetSavePath(targetSavePath);
        }
        GUILayout.EndHorizontal();
    }

    private void DrawMeshInfoGUI()
    {
        GUILayout.Space(10);
        GUILayout.Label("Mesh information", EditorStyles.boldLabel);

        targetSprite = (Sprite)EditorGUILayout.ObjectField("Target sprite", targetSprite, typeof(Sprite), false);

        EditorGUI.BeginDisabledGroup(targetSprite == null);

        GUILayout.Space(5);
        verticesType = (VerticesType)EditorGUILayout.EnumPopup("Target vertices", verticesType);

        GUILayout.Space(5);
        float sliderValue = GetTargetThickness();
        sliderValue = EditorGUILayout.Slider("Thickness", sliderValue, THICKNESS_MIN, THICKNESS_MAX);
        SetTargetThickness(sliderValue);
        EditorGUI.EndDisabledGroup();
    }

    private void DrawSaveAssetGUI()
    {
        Rect buttonRect = new Rect(position.width - (DEFAULT_BUTTON_WIDTH + BUTTON_SPACE),
        position.height - (DEFAULT_BUTTON_HEIGHT + BUTTON_SPACE),
        DEFAULT_BUTTON_WIDTH,
        DEFAULT_BUTTON_HEIGHT);

        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(targetSprite == null);
        if (GUI.Button(buttonRect, "Generate"))
        {
            if (CheckDataValidation())
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(GetMeshAssetPath());
                if (mesh == null)
                {
                    mesh = Create3DMeshFromSprite();
                    AssetDatabase.CreateAsset(mesh, GetMeshAssetPath());
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

                    Debug.Log("Mesh generator : Mesh create success!");
                }
                else
                {
                    //Mesh update
                    Mesh newMesh = Create3DMeshFromSprite();

                    mesh.Clear();
                    mesh.SetVertices(newMesh.vertices.ToList());
                    mesh.triangles = newMesh.triangles;
                    mesh.normals = newMesh.normals;

                    EditorUtility.SetDirty(mesh);

                    Debug.Log("Mesh generator : Mesh update success!");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
        EditorGUI.EndDisabledGroup();
    }

    private string GetMeshAssetPath()
    {
        return FileUtil.GetProjectRelativePath($"{GetTargetSavePath()}/{targetSprite.name}.asset");
    }

    private bool CheckDataValidation()
    {
        string targetPath = GetTargetSavePath();
        if (string.IsNullOrEmpty(targetPath) || !Directory.Exists(targetPath))
        {
            Debug.LogAssertion("Mesh generator : Path is empty or wrong.");
            return false;
        }

        if (targetSprite == null)
        {
            Debug.LogAssertion("Mesh generator : Sprite is empty.");
            return false;
        }

        return true;
    }

    private string GetTargetSavePath()
    {
        return EditorPrefs.GetString(KEY_SAVE_PATH, Application.dataPath);
    }

    private void SetTargetSavePath(string path)
    {
        EditorPrefs.SetString(KEY_SAVE_PATH, path);
    }

    private float GetTargetThickness()
    {
        return EditorPrefs.GetFloat(KEY_THICKNESS, 1f);
    }

    private void SetTargetThickness(float thickness)
    {
        EditorPrefs.SetFloat(KEY_THICKNESS, thickness);
    }

    private Mesh Create3DMeshFromSprite()
    {
        Vector2[] spriteVertices;
        ushort[] spriteTriangles;
        if (GetVerticesAndTriangles(out spriteVertices, out spriteTriangles))
        {
            int vertexCount = spriteVertices.Length;
            Vector3[] meshVertices = new Vector3[vertexCount * 2];
            int[] meshTriangles = new int[spriteTriangles.Length * 2 + vertexCount * 6];

            // 앞, 뒷면의 정점 생성
            float thickness = GetTargetThickness();
            for (int i = 0; i < vertexCount; i++)
            {
                Vector2 vertex = spriteVertices[i];
                meshVertices[i] = new Vector3(vertex.x, vertex.y, -thickness / 2); // 앞면
                meshVertices[i + vertexCount] = new Vector3(vertex.x, vertex.y, thickness / 2); // 뒷면
            }

            // 앞, 뒷면 트라이앵글 생성
            for (int i = 0; i < spriteTriangles.Length; i++)
            {
                meshTriangles[i] = spriteTriangles[i];
                meshTriangles[spriteTriangles.Length + i] = spriteTriangles[spriteTriangles.Length - 1 - i] + vertexCount;
            }

            Dictionary<VertexLine, int> vertexLines = OrganizeVertexLines(spriteTriangles);

            //옆면 삼각형
            int offset = spriteTriangles.Length * 2;
            int startSideIndex = 0;
            for (int i = 0; i < spriteTriangles.Length / 3; i++)
            {
                int startCount = i * 3;
                for (int j = 0; j < 3; j++)
                {
                    int next = (startCount + j + 1) % 3;
                    VertexLine line = new VertexLine(spriteTriangles[j + startCount], spriteTriangles[next + startCount]);

                    if (vertexLines.ContainsKey(line) && vertexLines[line] < 2)
                    {
                        //Debug.Log($"in : {spriteTriangles[j + startCount]}, {spriteTriangles[next + startCount]}");

                        int leftUp = spriteTriangles[j + startCount] + spriteVertices.Length;
                        int rightUp = spriteTriangles[next + startCount] + spriteVertices.Length;
                        int leftDown = spriteTriangles[j + startCount];
                        int rightDown = spriteTriangles[next + startCount];

                        meshTriangles[offset + startSideIndex * 6] = leftUp;
                        meshTriangles[offset + startSideIndex * 6 + 1] = rightUp;
                        meshTriangles[offset + startSideIndex * 6 + 2] = rightDown;
                        meshTriangles[offset + startSideIndex * 6 + 3] = rightDown;
                        meshTriangles[offset + startSideIndex * 6 + 4] = leftDown;
                        meshTriangles[offset + startSideIndex * 6 + 5] = leftUp;

                        startSideIndex++;
                    }
                }
            }


            // 메쉬 생성
            Mesh mesh = new Mesh();
            mesh.vertices = meshVertices;
            mesh.triangles = meshTriangles;
            mesh.RecalculateNormals();

            return mesh;
        }

        return null;
    }

    private Dictionary<VertexLine, int> OrganizeVertexLines(ushort[] triangles)
    {
        Dictionary<VertexLine, int> vertexLines = new Dictionary<VertexLine, int>();

        int targetLength = triangles.Length / 3;
        for (int i = 0; i < targetLength; i++)
        {
            int startCount = i * 3;
            for (int j = 0; j < 3; j++)
            {
                int next = (startCount + j + 1) % 3;
                VertexLine line = new VertexLine(triangles[j + startCount], triangles[next + startCount]);

                if (vertexLines.ContainsKey(line))
                {
                    vertexLines[line] += 1;
                }
                else
                {
                    vertexLines.Add(line, 1);
                }
            }
        }

        // foreach (var d in vertexLines)
        // {
        //     Debug.Log($"{d.Key.first}, {d.Key.second} : {d.Value}");
        // }

        return vertexLines;
    }

    private bool GetVerticesAndTriangles(out Vector2[] vertices, out ushort[] triangles)
    {
        switch (verticesType)
        {
            case VerticesType.PhsyicsShape:
                List<Vector2[]> physicsShapes = new List<Vector2[]>();

                // 스프라이트의 물리 모양 개수를 가져옵니다.
                int shapeCount = targetSprite.GetPhysicsShapeCount();

                // 각 물리 모양을 리스트에 추가합니다.
                for (int i = 0; i < shapeCount; i++)
                {
                    List<Vector2> shape = new List<Vector2>();
                    targetSprite.GetPhysicsShape(i, shape);
                    physicsShapes.Add(shape.ToArray());
                }

                // 물리 모양을 메쉬 데이터로 변환합니다.
                List<Vector2> shapeVertices = new List<Vector2>();
                List<ushort> shapeTriangles = new List<ushort>();
                int vertexIndex = 0;

                foreach (Vector2[] shape in physicsShapes)
                {
                    // 각 shape의 정점 추가
                    for (int i = 0; i < shape.Length; i++)
                    {
                        shapeVertices.Add(shape[i]);
                    }

                    // 삼각형 인덱스 생성
                    for (int i = 0; i < shape.Length - 2; i++)
                    {
                        shapeTriangles.Add((ushort)(vertexIndex + i + 2));
                        shapeTriangles.Add((ushort)(vertexIndex + i + 1));
                        shapeTriangles.Add((ushort)vertexIndex);
                    }

                    vertexIndex += shape.Length;
                }

                vertices = shapeVertices.ToArray();
                triangles = shapeTriangles.ToArray();
                return true;
            case VerticesType.OutLine:
                vertices = targetSprite.vertices;
                triangles = targetSprite.triangles;
                return true;
            default:
                vertices = null;
                triangles = null;
                return false;
        }
    }
}

public struct VertexLine
{
    public ushort first;
    public ushort second;

    public VertexLine(ushort first, ushort second)
    {
        if (first < second)
        {
            this.first = first;
            this.second = second;
        }
        else
        {
            this.first = second;
            this.second = first;
        }
    }
}