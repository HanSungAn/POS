using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Core.Interfaces;
using PosSystem.Core.Services;
using PosSystem.Infrastructure.Data;
using PosSystem.Infrastructure.Peripherals;
using PosSystem.Infrastructure.Protocols;
using PosSystem.Infrastructure.Sync;
using PosSystem.Wpf.ViewModels;

namespace PosSystem.Wpf;

/// <summary>
/// WPF 애플리케이션의 시작점(진입점)입니다.
/// appsettings.json 설정을 읽고, 의존성 주입(DI) 컨테이너에 서비스와 ViewModel을 등록한 뒤 메인 창을 띄웁니다.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    private const string FallbackConnectionString =
        "Server=localhost;Database=PosSystemDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. 실행 폴더의 appsettings.json에서 DB 연결 문자열과 본사 API 주소를 읽어옵니다.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        string connectionString = configuration.GetConnectionString("PosDb") ?? FallbackConnectionString;
        string hqApiBaseUrl = configuration["HqApi:BaseUrl"] ?? "http://localhost:5180";

        // 2. DI(의존성 주입) 컨테이너 구성
        var services = new ServiceCollection();

        // MS SQL Server용 EF Core DbContextFactory 등록
        services.AddDbContextFactory<PosDbContext>(options =>
            options.UseSqlServer(connectionString));

        // 저장소(EF Core + Dapper) 등록
        services.AddSingleton<IPosRepository, PosRepository>();
        services.AddSingleton<ISettlementRepository>(sp =>
            new DapperSettlementRepository(connectionString, sp.GetRequiredService<IDbContextFactory<PosDbContext>>()));

        // VAN 전문 코덱 및 결제 단말기 클라이언트 등록
        services.AddSingleton<IVanProtocolCodec, VanPacketCodec>();
        services.AddSingleton<MockVanTerminalClient>();
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<MockVanTerminalClient>());

        // 주변기기(영수증 프린터, 캐시드로어, 바코드 스캐너, 고객용 디스플레이) 등록
        services.AddSingleton<IReceiptPrinter, EscPosReceiptPrinter>();
        services.AddSingleton<ICashDrawer, SimulatedCashDrawer>();
        services.AddSingleton<IBarcodeScanner, SerialBarcodeScanner>();
        services.AddSingleton<ICustomerDisplay, SimulatedCustomerDisplay>();

        // 본사 REST API 통신용 HttpClient 등록
        services.AddHttpClient<ISyncService, HqApiSyncClient>(client =>
        {
            client.BaseAddress = new Uri(hqApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        // 핵심 비즈니스 서비스 및 기능별 ViewModel 등록
        services.AddSingleton<OrderService>();
        services.AddSingleton<PosSaleViewModel>();
        services.AddSingleton<OrderHistoryViewModel>();
        services.AddSingleton<InventorySettlementViewModel>();
        services.AddSingleton<PeripheralMonitorViewModel>();
        services.AddSingleton<HqSyncDiagnosticsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        // 3. 메인 창에 MainViewModel을 DataContext로 연결하고 화면을 표시합니다.
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        var vm = _serviceProvider.GetRequiredService<MainViewModel>();
        mainWindow.DataContext = vm;
        mainWindow.Show();

        // 4. DB 테이블 초기화 및 초기 데이터 로드
        await vm.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
