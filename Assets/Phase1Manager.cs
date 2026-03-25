using UnityEngine;
using TMPro;

public class Phase1Manager : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI statusText;

    private float _timer = 0f;
    private int _tick = 0;

    void Start()
    {
        if (statusText != null)
            statusText.text = "Phase 1 starting...";

        Debug.Log("[Phase1] Start() called successfully.");
    }

    void Update()
    {
        _timer += Time.deltaTime;

        if (_timer >= 1f)
        {
            _timer = 0f;
            _tick++;

            string msg = $"Phase 1 — Passthrough OK\n" +
                         $"Tick: {_tick}\n" +
                         $"FPS: {(1f / Time.deltaTime):F1}";

            if (statusText != null)
                statusText.text = msg;

            Debug.Log($"[Phase1] Tick {_tick}");
        }
    }
}