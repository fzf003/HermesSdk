
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace HermesAgent.Sdk.AgentAdapter.MicrosoftAgent
{
    public static class ChatMessageExtensions
    {
        /// <summary>
        /// 检查消息列表中是否包含媒体数据
        /// </summary>
        public static bool HasMediaContent(this IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages)
            => messages.Any(m => m.HasMediaContent());

        /// <summary>
        /// 检查单条消息是否包含媒体数据
        /// </summary>
        public static bool HasMediaContent(this Microsoft.Extensions.AI.ChatMessage message)
            => message.Contents.Any(c => c is DataContent or UriContent);

        /// <summary>
        /// 提取所有媒体内容
        /// </summary>
        public static IEnumerable<AIContent> GetMediaContents(this IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages)
            => messages.SelectMany(m => m.Contents)
                       .Where(c => c is DataContent or UriContent);

        /// <summary>
        /// 按类型分组统计
        /// </summary>
        public static MediaSummary GetMediaSummary(this IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages)
        {
            var allContents = messages.SelectMany(m => m.Contents).ToList();
            return new MediaSummary
            {
                TextCount = allContents.OfType<TextContent>().Count(),
                DataCount = allContents.OfType<DataContent>().Count(),
                UriCount = allContents.OfType<UriContent>().Count(),
                DataContents = allContents.OfType<DataContent>().ToList(),
                UriContents = allContents.OfType<UriContent>().ToList(),
            };
        }
    }

    public record MediaSummary
    {
        public int TextCount { get; init; }
        public int DataCount { get; init; }
        public int UriCount { get; init; }
        public bool HasMedia => DataCount > 0 || UriCount > 0;

        public List<DataContent> DataContents { get; init; } = [];
        public List<UriContent> UriContents { get; init; } = [];
    }
}
