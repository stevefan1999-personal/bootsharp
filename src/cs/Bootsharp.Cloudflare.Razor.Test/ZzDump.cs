namespace Bootsharp.Cloudflare.Razor.Tests;

public class ZzDump
{
    [Fact]
    public void Dump ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Home.cshtml", """
            @using System.Globalization
            @model string
            @param int count
            <div class="card" data-n="@count">
                <a href="@Model" title="@Model">@Model</a>
                <br/>
                @if (count > 0)
                {
                    <span>@count</span>
                }
            </div>
            @functions {
                static string Tag => "x";
            }
            """));
        File.WriteAllText("/tmp/claude-1000/-home-steve-git-github-com-elringus-bootsharp/b984f2e4-9983-4d75-a505-5a38d31f33a6/scratchpad/dump-golden.txt",
            run.Report + "\n=====\n" + run.Code);

        var ws = CshtmlHarness.Run(new Page("Views/W.cshtml", """
            @attribute [System.Obsolete]
            <p>a</p>
            """));
        File.WriteAllText("/tmp/claude-1000/-home-steve-git-github-com-elringus-bootsharp/b984f2e4-9983-4d75-a505-5a38d31f33a6/scratchpad/dump-attr.txt",
            ws.Report + "\n=====\n" + ws.Code);
    }
}
