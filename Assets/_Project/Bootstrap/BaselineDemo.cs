using System;
using System.Threading;
using UnityEngine;
using Xiaosu.CompanionAI.LLM;

namespace Xiaosu.Bootstrap
{
    public sealed class BaselineDemo : MonoBehaviour
    {
        private readonly MockLlmProvider provider = new MockLlmProvider();
        private readonly MockAvatar avatar = new MockAvatar();
        private CancellationTokenSource turn;
        private string input = string.Empty;
        private string output = string.Empty;
        private string status = "Ready";

        private void Awake()
        {
            var camera = new GameObject("Demo Camera").AddComponent<Camera>();
            camera.transform.SetParent(transform);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
            camera.allowHDR = false;
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, Mathf.Min(Screen.width - 32, 520), 220), GUI.skin.box);
            GUILayout.Label("Xiaosu | FUMI-42 Mock Demo");
            GUILayout.Label("Type a message to start a simulated response.");
            input = GUILayout.TextField(input, 300);
            GUILayout.BeginHorizontal();
            using (new GUIEnabledScope(turn == null && !string.IsNullOrWhiteSpace(input)))
                if (GUILayout.Button("Send")) Send();
            using (new GUIEnabledScope(turn != null))
                if (GUILayout.Button("Cancel")) turn?.Cancel();
            GUILayout.EndHorizontal();
            GUILayout.Label($"Status: {status}   Expression: {avatar.Expression}");
            GUILayout.Label(output);
            GUILayout.EndArea();
        }

        private async void Send()
        {
            var active = new CancellationTokenSource();
            turn = active;
            output = string.Empty;
            status = "Streaming";
            avatar.SetExpression("neutral");
            var request = new LlmChatRequest();
            request.Messages.Add(new LlmChatMessage("user", input));
            try
            {
                var result = await provider.StreamChatAsync(request, item =>
                {
                    if (active.IsCancellationRequested) return;
                    if (item.EventType == LlmEventType.TextDelta) output += item.Text;
                    if (item.EventType == LlmEventType.EmotionTag) avatar.SetExpression(item.TagValue);
                }, active.Token);
                status = result.IsCancelled ? "Cancelled" : result.IsSuccess ? "Complete" : "Failed";
            }
            catch (Exception exception)
            {
                status = "Failed";
                Debug.LogException(exception, this);
            }
            finally
            {
                turn = null;
                active.Dispose();
            }
        }

        private void OnDisable() => turn?.Cancel();

        private void OnDestroy()
        {
            avatar.Dispose();
            provider.Dispose();
        }

        private readonly struct GUIEnabledScope : IDisposable
        {
            private readonly bool previous;

            public GUIEnabledScope(bool enabled)
            {
                previous = GUI.enabled;
                GUI.enabled = enabled && previous;
            }

            public void Dispose() => GUI.enabled = previous;
        }
    }
}
