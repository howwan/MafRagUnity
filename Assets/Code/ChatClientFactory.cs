// Migration: Semantic Kernel -> Microsoft Agent Framework (REQ-02, REQ-09, NFR-04)
// Unified IChatClient construction for the local OpenAI-compatible endpoint.
// Replaces Kernel.CreateBuilder().AddOpenAIChatCompletion(...) calls throughout the project.
// See https://learn.microsoft.com/agent-framework/migration-guide/from-semantic-kernel

using System;
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

/// <summary>
/// Builds an <see cref="IChatClient"/> pointing at the local LLM endpoint.
/// The endpoint, model, and key are kept identical to the original Semantic Kernel configuration.
/// </summary>
public static class ChatClientFactory
{
    public const string Endpoint = "http://192.168.3.166:11434/v1";
    public const string Model = "qwen3.6:35b-a3b-mtp-q4_K_M";
    public const string ApiKey = "ollama";

    /// <summary>
    /// Creates an <see cref="IChatClient"/> for the configured local model.
    /// Equivalent to the previous AddOpenAIChatCompletion / AddAzureOpenAIChatCompletion setup.
    /// </summary>
    public static IChatClient CreateLocal(string model = Model)
    {
        return Create(Endpoint, ApiKey, model);
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> for an arbitrary endpoint / key / model.
    /// Used by RAG (and others) so the Agent (Chat) and Embedding endpoints are fully independent.
    /// </summary>
    public static IChatClient Create(string endpoint, string apiKey, string model)
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        var openAIClient = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        return openAIClient.GetChatClient(model).AsIChatClient();
    }
}
