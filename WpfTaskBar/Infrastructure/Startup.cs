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

            services.AddSingleton<ChromeTabManager>();
            services.AddSingleton<Http2StreamHandler>();
            services.AddSingleton<WebView2Handler>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            app.UseCors();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/stream")
                {
                    // HTTP/2 ストリーミングエンドポイント（サーバー→クライアント）
                    var http2Handler = app.ApplicationServices.GetRequiredService<Http2StreamHandler>();
                    await http2Handler.HandleStreamAsync(context);
                }
                else if (context.Request.Path == "/message" && context.Request.Method == "POST")
                {
                    // HTTP/2 メッセージエンドポイント（クライアント→サーバー）
                    var http2Handler = app.ApplicationServices.GetRequiredService<Http2StreamHandler>();
                    await http2Handler.HandleMessageAsync(context);
                }
                else
                {
                    await next();
                }
            });
        }
    }
} 