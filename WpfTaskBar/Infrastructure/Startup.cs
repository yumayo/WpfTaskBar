using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.WebSockets;

namespace WpfTaskBar
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddCors(options =>
            {
                options.AddDefaultPolicy(builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader();
                });
            });

            // サービスを追加
            services.AddSingleton<ChromeTabManager>();
            services.AddSingleton<WebSocketHandler>();
            services.AddSingleton<WindowManager>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            app.UseCors();

            // WebSocketサポートを有効化
            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30), // 30秒ごとにKeep-Alive
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            // WebSocketエンドポイントを設定
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ws")
                {
                    var webSocketHandler = app.ApplicationServices.GetRequiredService<WebSocketHandler>();
                    await webSocketHandler.HandleWebSocketAsync(context);
                }
                else
                {
                    await next();
                }
            });
        }
    }
} 