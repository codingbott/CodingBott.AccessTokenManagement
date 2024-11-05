﻿// Copyright (c) Duende Software. All rights reserved.
// See LICENSE in the project root for license information.


using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Security.Claims;

namespace Duende.AccessTokenManagement.Tests;

public class GenericHost
{
    public GenericHost(string baseAddress = "https://server")
    {
        if (baseAddress.EndsWith("/")) baseAddress = baseAddress.Substring(0, baseAddress.Length - 1);
        _baseAddress = baseAddress;
    }

    protected readonly string _baseAddress;
    IServiceProvider _appServices = default!;

    public Assembly HostAssembly { get; set; } = default!;
    public bool IsDevelopment { get; set; } = default!;

    public TestServer Server { get; private set; } = default!;
    public TestBrowserClient BrowserClient { get; set; } = default!;
    public HttpClient HttpClient { get; set; } = default!;
    public HttpMessageHandler HttpMessageHandler { get; set; } = default!;
    public TestLoggerProvider Logger { get; set; } = new TestLoggerProvider();

    public T Resolve<T>()
        where T : notnull
    {
        // not calling dispose on scope on purpose
        return _appServices.GetRequiredService<IServiceScopeFactory>().CreateScope().ServiceProvider.GetRequiredService<T>();
    }

    public string Url(string? path = null)
    {
        path = path ?? String.Empty;
        if (!path.StartsWith("/")) path = "/" + path;
        return _baseAddress + path;
    }

    public async Task InitializeAsync()
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();

                builder.ConfigureAppConfiguration((context, b) =>
                {
                    if (HostAssembly is not null)
                    {
                        context.HostingEnvironment.ApplicationName = HostAssembly.GetName().Name!;
                    }
                });

                if (IsDevelopment)
                {
                    builder.UseSetting("Environment", "Development");
                }
                else
                {
                    builder.UseSetting("Environment", "Production");
                }

                builder.ConfigureServices(ConfigureServices);
                builder.Configure(ConfigureApp);
            });

        // Build and start the IHost
        var host = await hostBuilder.StartAsync();

        Server = host.GetTestServer();
        BrowserClient = new TestBrowserClient(Server.CreateHandler());
        HttpClient = Server.CreateClient();
        HttpMessageHandler = Server.CreateHandler();
    }

    public event Action<IServiceCollection> OnConfigureServices = services => { };
    public event Action<IApplicationBuilder> OnConfigure = app => { };

    void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(options =>
        {
            options.SetMinimumLevel(LogLevel.Debug);
            options.AddProvider(Logger);
        });

        OnConfigureServices(services);
    }

    void ConfigureApp(IApplicationBuilder app)
    {
        _appServices = app.ApplicationServices;
            
        OnConfigure(app);

        ConfigureSignin(app);
        ConfigureSignout(app);
    }



    void ConfigureSignout(IApplicationBuilder app)
    {
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path == "/__signout")
            {
                await ctx.SignOutAsync();
                ctx.Response.StatusCode = 204;
                return;
            }

            await next();
        });
    }
        
    public async Task RevokeSessionCookieAsync()
    {
        var response = await BrowserClient.GetAsync(Url("__signout"));
        response.StatusCode.ShouldBe((HttpStatusCode)204);
    }


    void ConfigureSignin(IApplicationBuilder app)
    {
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path == "/__signin")
            {
                if (_userToSignIn is not object)
                {
                    throw new Exception("No User Configured for SignIn");
                }

                var props = _propsToSignIn ?? new AuthenticationProperties();
                await ctx.SignInAsync(_userToSignIn, props);
                    
                _userToSignIn = null;
                _propsToSignIn = null;

                ctx.Response.StatusCode = 204;
                return;
            }

            await next();
        });
    }
        
    ClaimsPrincipal? _userToSignIn = default!;
    AuthenticationProperties? _propsToSignIn = default!;
        
    public async Task IssueSessionCookieAsync(params Claim[] claims)
    {
        _userToSignIn = new ClaimsPrincipal(new ClaimsIdentity(claims, "test", "name", "role"));
        var response = await BrowserClient.GetAsync(Url("__signin"));
        response.StatusCode.ShouldBe((HttpStatusCode)204);
    }
    public Task IssueSessionCookieAsync(AuthenticationProperties props, params Claim[] claims)
    {
        _propsToSignIn = props;
        return IssueSessionCookieAsync(claims);
    }
    public Task IssueSessionCookieAsync(string sub, params Claim[] claims)
    {
        return IssueSessionCookieAsync(claims.Append(new Claim("sub", sub)).ToArray());
    }
}