namespace DomainReloadCases
{
    public static class CaseProbe
    {
        /// <summary>测试在每个 Play 会话中输出一次，用于统计 Application.logMessageReceived 上残留的处理器数量。</summary>
        public const string Message = "[DomainReloadCases] probe";
    }
}
