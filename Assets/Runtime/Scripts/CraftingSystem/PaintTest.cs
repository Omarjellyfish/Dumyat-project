using UnityEngine;

namespace DumyatRuntime
{
    public class PaintTest : MonoBehaviour
    {
        [SerializeField] PaintBrush _paintBrush;
        [SerializeField] Color _color = Color.black;
        [SerializeField] float _brushSize = 0.05f;

        void Update()
        {
            // press space to test paint at center of wood
            if (Input.GetKeyDown(KeyCode.Space))
            {
                WoodPaintSurface wood = GetComponent<WoodPaintSurface>();
                Debug.Log("RenderTexture: " + wood.PaintTexture);
                Debug.Log("RenderTexture created: " + wood.PaintTexture.IsCreated());
                _paintBrush.PaintAt(wood, new Vector2(0.5f, 0.5f), _color, _brushSize);
                Debug.Log("PaintAt called at center");
            }
        }
    } 
} 