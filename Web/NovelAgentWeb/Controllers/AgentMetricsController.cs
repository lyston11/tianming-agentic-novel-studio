using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

/// <summary>
/// Agent 架构修复性能监控 API
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentMetricsController : ControllerBase
{
    /// <summary>
    /// 获取 Agent 架构修复性能指标报告
    /// </summary>
    [HttpGet("architecture-fixes")]
    public IActionResult GetArchitectureFixesMetrics()
    {
        var report = AgentArchitectureMetrics.Instance.GetPerformanceReport();
        return Ok(new
        {
            timestamp = DateTime.UtcNow,
            report = report
        });
    }

    /// <summary>
    /// 重置性能指标（仅开发环境）
    /// </summary>
    [HttpPost("architecture-fixes/reset")]
    public IActionResult ResetArchitectureFixesMetrics()
    {
        if (!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        {
            return Forbid();
        }

        AgentArchitectureMetrics.Instance.Reset();
        return Ok(new { message = "Metrics reset successfully" });
    }
}
