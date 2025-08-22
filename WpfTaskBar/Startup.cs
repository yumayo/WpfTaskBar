using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

            // WebSocket関連のサービスを追加
            services.AddSingleton<TabManager>();
            services.AddSingleton<WebSocketHandler>();
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
            app.UseWebSockets();

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