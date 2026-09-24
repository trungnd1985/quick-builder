using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.ArchitectureTests.Fixtures;

// Deliberately wrong access declarations, one per rule in AuthorizationDeclarationTests.
// They exist only to prove each rule fires. They live in the test assembly, which no host
// loads, so they are never routable.

public sealed class UndeclaredFixtureController : ControllerBase
{
    [HttpGet("fixture/undeclared")]
    public IActionResult Get() => Ok();
}

public sealed class DoublyDeclaredFixtureController : ControllerBase
{
    [HttpGet("fixture/doubly")]
    [RequireSignedIn]
    [AllowAnonymous]
    public IActionResult Get() => Ok();
}

public sealed class UnlistedAnonymousFixtureController : ControllerBase
{
    [HttpGet("fixture/anonymous")]
    [AllowAnonymous]
    public IActionResult Get() => Ok();
}

public sealed class UnknownCapabilityFixtureController : ControllerBase
{
    [HttpGet("fixture/unknown")]
    [RequireCapability("fixture.nothing.registered")]
    public IActionResult Get() => Ok();
}

[Route("api/v1/fixture")]
public sealed class SiteScopedWithoutSiteIdFixtureController : ControllerBase
{
    [HttpPost("things")]
    [RequireCapability("sites.host_names.manage")]
    public IActionResult Post() => Ok();
}

/// <summary>The control: declared correctly, must not be flagged.</summary>
[Route("api/v1/fixture/{siteId:guid}")]
public sealed class WellDeclaredFixtureController : ControllerBase
{
    [HttpGet("things")]
    [RequireCapability("sites.view")]
    public IActionResult Get() => Ok();

    [HttpGet("mine")]
    [RequireSignedIn]
    public IActionResult Mine() => Ok();
}
