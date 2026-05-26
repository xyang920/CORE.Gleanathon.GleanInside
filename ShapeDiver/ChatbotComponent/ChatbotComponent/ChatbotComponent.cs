using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Grasshopper.Kernel;

namespace ChatbotComponent
{
    public class GleanChatbotComponent : GH_Component
    {
        private static readonly HttpClient _http = new HttpClient();

        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public const string Version = "1.0.2";

        private string _persistentHistory = "";

        public GleanChatbotComponent()
            : base("Glean Chatbot", "GleanBot",
                $"Send messages to the Glean Agent API and receive AI responses. " +
                $"Conversation history is stored inside the component 鈥?no wire loop needed. " +
                $"Toggle Reset to True to start a new session. " +
                $"v{Version}",
                "ShapeDiver", "AI")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Message", "Msg",
                "User message to send to Glean", GH_ParamAccess.item);

            pManager.AddTextParameter("API Token", "Tok",
                "Glean API bearer token", GH_ParamAccess.item);

            pManager.AddTextParameter("Domain", "Dom",
                "Glean tenant domain slug (e.g. 'mycompany' resolves to mycompany-be.glean.com)",
                GH_ParamAccess.item);

            pManager.AddBooleanParameter("Reset", "Rst",
                "Set to True to clear the stored conversation history and start a new session.",
                GH_ParamAccess.item, false);

            pManager.AddTextParameter("Agent ID", "AgentID",
                "Glean agent ID to invoke (from the agent's Publishing > API settings). Leave empty to use the default AI assistant.",
                GH_ParamAccess.item, "42349c7312dd4630aae2c75daa64803b");

            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Response", "Resp",
                "Text response from the Glean AI agent", GH_ParamAccess.item);

            pManager.AddTextParameter("History", "Hist",
                "Updated conversation history JSON. Feed this back into the History input to continue the conversation.",
                GH_ParamAccess.item);

            pManager.AddTextParameter("Status", "Stat",
                "\"OK\" on success, or an error description on failure", GH_ParamAccess.item);

            pManager.AddTextParameter("Chat Log", "Log",
                "Human-readable conversation transcript. Connect to a Panel component to display chat history.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string message = "", token = "", domain = "", agent = "";
            bool reset = false;

            if (!DA.GetData(0, ref message)) return;
            if (!DA.GetData(1, ref token)) return;
            if (!DA.GetData(2, ref domain)) return;
            DA.GetData(3, ref reset);
            DA.GetData(4, ref agent);

            if (reset)
                _persistentHistory = "";

            if (string.IsNullOrWhiteSpace(message))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Message is empty 鈥?nothing sent.");
                DA.SetData(1, _persistentHistory);
                DA.SetData(3, BuildChatLog(DeserializeMessages(_persistentHistory)));
                return;
            }
            if (string.IsNullOrWhiteSpace(token))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "API Token is required.");
                return;
            }
            if (string.IsNullOrWhiteSpace(domain))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Domain is required.");
                return;
            }

            try
            {
                var (responseText, updatedHistory, chatLog) =
                    CallGleanChat(message, token, domain.Trim(), _persistentHistory, agent)
                    .GetAwaiter().GetResult();

                _persistentHistory = updatedHistory;

                DA.SetData(0, responseText);
                DA.SetData(1, _persistentHistory);
                DA.SetData(2, "OK");
                DA.SetData(3, chatLog);
            }
            catch (Exception ex)
            {
                string err = ex.InnerException?.Message ?? ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, err);
                DA.SetData(0, "");
                DA.SetData(1, _persistentHistory);   // return history unchanged so caller can retry
                DA.SetData(2, $"Error: {err}");
                DA.SetData(3, "");
            }
        }

        // 鈹€鈹€鈹€ Core API call 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

        private async Task<(string responseText, string updatedHistory, string chatLog)> CallGleanChat(
            string userMessage, string token, string domain, string historyJson, string agentId)
        {
            string url = $"https://{domain}-be.glean.com/rest/api/v1/chat";

            // Restore or initialise conversation state
            ConversationState state;
            try
            {
                state = string.IsNullOrWhiteSpace(historyJson)
                    ? new ConversationState()
                    : JsonSerializer.Deserialize<ConversationState>(historyJson, _json)
                      ?? new ConversationState();
            }
            catch
            {
                state = new ConversationState();
            }

            // Append the new user turn
            state.Messages.Add(new ChatMessage
            {
                Author = "USER",
                Fragments = new List<Fragment> { new Fragment { Text = userMessage } }
            });

            // Build request body
            var reqBody = new ChatRequest
            {
                Messages = state.Messages,
                // Resume existing Glean session if we have one
                ChatSessionContext = string.IsNullOrEmpty(state.SessionId)
                    ? null
                    : new ChatSessionContext { SessionId = state.SessionId },
                AgentConfig = string.IsNullOrWhiteSpace(agentId)
                    ? null
                    : new AgentConfig { AgentId = agentId }
            };

            string bodyJson = JsonSerializer.Serialize(reqBody, _json);

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            var httpResp = await _http.SendAsync(req).ConfigureAwait(false);
            string raw = await httpResp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!httpResp.IsSuccessStatusCode)
                throw new Exception(
                    $"HTTP {(int)httpResp.StatusCode} {httpResp.ReasonPhrase}: {raw}");

            // Parse response
            var chatResp = JsonSerializer.Deserialize<ChatResponse>(raw, _json);

            // Persist the session ID for subsequent turns
            if (!string.IsNullOrEmpty(chatResp?.SessionInfo?.SessionId))
                state.SessionId = chatResp.SessionInfo.SessionId;

            // Extract the assistant reply text
            string responseText = ExtractAssistantText(chatResp?.Messages);

            // Append assistant turn to history
            if (!string.IsNullOrEmpty(responseText))
                state.Messages.Add(new ChatMessage
                {
                    Author = "GLEAN_AI",
                    Fragments = new List<Fragment> { new Fragment { Text = responseText } }
                });

            string chatLog = BuildChatLog(state.Messages);
            return (responseText, JsonSerializer.Serialize(state, _json), chatLog);
        }

        private List<ChatMessage> DeserializeMessages(string historyJson)
        {
            if (string.IsNullOrWhiteSpace(historyJson)) return new List<ChatMessage>();
            try
            {
                return JsonSerializer.Deserialize<ConversationState>(historyJson, _json)?.Messages
                       ?? new List<ChatMessage>();
            }
            catch { return new List<ChatMessage>(); }
        }

        private static string ExtractAssistantText(List<ChatMessage> messages)
        {
            if (messages == null) return "";
            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                if (msg.Author != "GLEAN_AI" || msg.Fragments == null) continue;
                foreach (var frag in msg.Fragments)
                    if (!string.IsNullOrEmpty(frag.Text))
                        sb.Append(frag.Text);
            }
            return sb.ToString();
        }

        private static string BuildChatLog(List<ChatMessage> messages)
        {
            if (messages == null || messages.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                if (msg.Fragments == null) continue;
                string label = msg.Author == "USER" ? "You" : "Bot";
                var text = new StringBuilder();
                foreach (var frag in msg.Fragments)
                    if (!string.IsNullOrEmpty(frag.Text))
                        text.Append(frag.Text);
                if (text.Length == 0) continue;
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine($"{label}: {text}");
            }
            return sb.ToString().TrimEnd();
        }

        // 鈹€鈹€鈹€ JSON models 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

        // Persisted between turns; serialised as the History output
        private class ConversationState
        {
            [JsonPropertyName("sessionId")]  public string SessionId { get; set; }
            [JsonPropertyName("messages")]   public List<ChatMessage> Messages { get; set; } = new();
        }

        private class ChatRequest
        {
            [JsonPropertyName("messages")]           public List<ChatMessage> Messages { get; set; }
            [JsonPropertyName("chatSessionContext")] public ChatSessionContext ChatSessionContext { get; set; }
            [JsonPropertyName("agentConfig")]        public AgentConfig AgentConfig { get; set; }
        }

        private class ChatSessionContext
        {
            [JsonPropertyName("sessionId")] public string SessionId { get; set; }
        }

        private class AgentConfig
        {
            [JsonPropertyName("agentId")] public string AgentId { get; set; }
        }

        private class ChatMessage
        {
            [JsonPropertyName("author")]    public string Author { get; set; }
            [JsonPropertyName("fragments")] public List<Fragment> Fragments { get; set; }
        }

        private class Fragment
        {
            [JsonPropertyName("text")] public string Text { get; set; }
        }

        private class ChatResponse
        {
            [JsonPropertyName("messages")]    public List<ChatMessage> Messages { get; set; }
            [JsonPropertyName("sessionInfo")] public SessionInfo SessionInfo { get; set; }
        }

        private class SessionInfo
        {
            [JsonPropertyName("sessionId")] public string SessionId { get; set; }
        }

        // 鈹€鈹€鈹€ Grasshopper metadata 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => ChatbotComponentInfo._icon;

        // Unique, stable GUID 鈥?never change this once the component is deployed
        public override Guid ComponentGuid => new Guid("3f8a1c9e-2b74-4d56-a831-7c4f9e2d10ab");
    }
}

