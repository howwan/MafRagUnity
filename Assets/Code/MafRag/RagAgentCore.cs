// MAF 智能体核心封装（D6 / FR-5）。
// 沿用现有 03.RagAgent 的 MAF 调用模式：ChatClientFactory.Create -> AsAIAgent -> AgentSession -> RunStreamingAsync。

using System;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UnityEngine;

namespace MafRag
{
    public class RagAgentCore
    {
        private ChatClientAgent _agent;
        private AgentSession _session;

        // 系统指令：仅依据 <context> 内知识作答，未知则明说，使用中文。
        private const string SystemInstructions = @"
你是一个基于本地知识库的问答助手。请仅依据用户消息中 <context> 标签内的内容回答问题；
如果上下文未涵盖该问题，请明确说明“知识库中未找到相关信息”，不要编造。
回答请使用简体中文，条理清晰。
";

        // 确保 Agent 已构建（embedding 配置变更时由外部重置后重建）
        public async Task EnsureAgentAsync()
        {
            if (_agent == null)
            {
                EmbeddingFactory.Reset();
                var chatClient = ChatClientFactory.Create(RagConfig.ChatEndpoint, RagConfig.ChatApiKey, RagConfig.ChatModel);
                _agent = chatClient.AsAIAgent(instructions: SystemInstructions);
            }
            else
            {
                await Task.CompletedTask;
            }
        }

        public async Task RunStreamingAsync(string prompt, Action<string> onToken, Action<string> onError)
        {
            try
            {
                await EnsureAgentAsync();
                _session ??= await _agent.CreateSessionAsync();
                await foreach (var update in _agent.RunStreamingAsync(prompt, _session, new ChatClientAgentRunOptions(new ChatOptions())))
                {
                    if (!string.IsNullOrEmpty(update.Text)) onToken?.Invoke(update.Text);
                    await Task.Yield();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RagAgentCore] 生成失败：{ex}");
                onError?.Invoke(ex.Message);
            }
        }
    }
}
