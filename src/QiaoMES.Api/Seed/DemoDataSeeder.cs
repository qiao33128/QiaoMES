using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QiaoMES.Equipment.Domain;
using QiaoMES.Equipment.Infrastructure.Persistence;
using QiaoMES.MasterData.Domain;
using QiaoMES.MasterData.Infrastructure.Persistence;
using QiaoMES.Production.Domain;
using QiaoMES.Production.Infrastructure.Persistence;
using QiaoMES.Quality.Domain;
using QiaoMES.Quality.Infrastructure.Persistence;
using QiaoMES.Reporting.Application;
using QiaoMES.Reporting.Domain;
using QiaoMES.Reporting.Infrastructure;
using QiaoMES.Reporting.Infrastructure.Persistence;
using QiaoMES.Shared;
using EquipmentEntity = QiaoMES.Equipment.Domain.Equipment;

namespace QiaoMES.Api.Seed;

/// <summary>
/// 演示数据生成器(仅开发环境)。
/// <para>
/// 目标:把「一条完整的 SMT 制造链路」灌进库里,让每个页面、每张报表、SPC 与 OEE 都有东西可看,
/// 同时保持 <b>可复现</b>(固定随机种子)、<b>可清理</b>(业务编码统一以 <c>DEMO-</c> 开头)、
/// <b>幂等</b>(每次先清理再重建)。
/// </para>
/// <para>
/// 数据布局:3 条 SMT 产线 × 5 个机台工位、3 个产品(各带 BOM 与工艺路线)、5 道工序、12 个不良代码、
/// 15 台设备(含停机历史)、2 个班次(含跨天夜班)、8 张工单(草稿 → 已完工)、
/// 每单若干 SN 及完整过站轨迹、来料批次与 IQC 谱系、IPQC / FQC 检验单、不合格处置、Andon 呼叫。
/// </para>
/// <para>
/// 时间线:实体构造时时间戳只能取"当前时刻",因此落库后统一用一条 <c>UPDATE</c> 按
/// <c>hashtext(业务键)</c> 把工单 / SN / 检验单铺开到最近 N 天 —— 报表的区间聚合、SPC 趋势、
/// 班次归属才有真实横轴。
/// </para>
/// </summary>
public sealed class DemoDataSeeder(
    MasterDataDbContext masterData,
    ProductionDbContext production,
    QualityDbContext quality,
    EquipmentDbContext equipment,
    ReportingDbContext reporting,
    DbConnection connection,
    IMetricsAggregator metricsAggregator,
    ICurrentUser currentUser,
    ILogger<DemoDataSeeder> logger)
{
    /// <summary>演示数据的统一编码前缀(清理时按它识别,业务数据完全不受影响)。</summary>
    public const string Prefix = "DEMO-";

    private const string OrderRemark = "[DEMO] 演示数据(可用 /api/dev/demo-data 一键清理)";

    private const string LineWorkshop = "SMT 一车间";

    private static readonly Random Random = new(20260916);

    /// <summary>
    /// 种子基准日期。<para>
    /// 🔴 必须用 UTC:Npgsql 只接受 <c>Kind = Utc</c> 的 DateTime 写入 <c>timestamptz</c>,
    /// 而 <c>DateTime.Today</c> / <c>DateTime.Now</c> 的 Kind 是 Local,直接写会抛
    /// 「Cannot write DateTime with Kind=Local to PostgreSQL type 'timestamp with time zone'」。
    /// 演示数据的时间线随后会被 <see cref="SpreadTimelineAsync"/> 重写,所以这里用 UTC 日期不影响口径。
    /// </para>
    /// </summary>
    private static DateTime TodayUtc => DateTime.UtcNow.Date;

    // ============================================================
    // 种子定义
    // ============================================================

    private static readonly (string Code, string Name, string Spec)[] ProductSeeds =
    [
        ("DEMO-A100", "智能手机主板", "A100-6L"),
        ("DEMO-B200", "车载摄像头模组", "B200-4L"),
        ("DEMO-C300", "智能手表主板", "C300-8L"),
    ];

    private static readonly (string Code, string Name, MaterialType Type, string Unit, string Spec)[] MaterialSeeds =
    [
        ("DEMO-M-PCB", "六层印制电路板", MaterialType.Raw, "片", "FR4-1.6mm"),
        ("DEMO-M-SOLDER", "无铅锡膏", MaterialType.Consumable, "g", "SAC305"),
        ("DEMO-M-STENCIL", "激光钢网", MaterialType.Consumable, "张", "0.12mm"),
        ("DEMO-M-CAP", "0402 贴片电容", MaterialType.Raw, "只", "100nF/50V"),
        ("DEMO-M-RES", "0402 贴片电阻", MaterialType.Raw, "只", "10kΩ/1%"),
        ("DEMO-M-IC", "主控 SoC 芯片", MaterialType.Raw, "颗", "BGA-441"),
        ("DEMO-M-SHIELD", "电磁屏蔽罩", MaterialType.Raw, "个", "0.2mm 不锈钢"),
        ("DEMO-M-CASE", "结构件外壳组件", MaterialType.SemiFinished, "套", "铝合金阳极氧化"),
    ];

    /// <summary>来料批次数量(按物料编码给一个像样的量级)。</summary>
    private static decimal LotQuantity(string materialCode) => materialCode switch
    {
        "DEMO-M-PCB" => 3_000m,
        "DEMO-M-CAP" => 500_000m,
        "DEMO-M-RES" => 400_000m,
        "DEMO-M-SOLDER" => 20_000m,
        "DEMO-M-STENCIL" => 200m,
        _ => 5_000m,
    };

    /// <summary>SMT 标准五道工序(顺序即工艺路线顺序,后两道为质检点)。</summary>
    private static readonly (string Code, string Name, int Seconds, bool Key)[] OperationSeeds =
    [
        ("DEMO-OP-SP", "锡膏印刷", 25, false),
        ("DEMO-OP-MT", "贴片", 40, false),
        ("DEMO-OP-RF", "回流焊", 30, false),
        ("DEMO-OP-AOI", "AOI 检测", 20, true),
        ("DEMO-OP-FT", "功能测试", 35, true),
    ];

    /// <summary>每条产线上的机台:后缀 → 工位名 / 设备名 / 型号。</summary>
    private static readonly (string Suffix, string StationName, string MachineName, string Model)[] MachineSeeds =
    [
        ("SP", "锡膏印刷工位", "锡膏印刷机", "DEK-03iX"),
        ("MT", "贴片工位", "高速贴片机", "NPM-W2"),
        ("RF", "回流焊工位", "回流焊炉", "Heller-1913MK5"),
        ("AOI", "AOI 工位", "AOI 检测机", "KY-8030-3"),
        ("FT", "测试工位", "功能测试机", "ICT-5000"),
    ];

    /// <summary>SMT 分工序预置的不良代码。</summary>
    private static readonly (string Code, string Name, string Category)[] DefectSeeds =
    [
        ("DEMO-D-SOLDER-BRIDGE", "锡桥(连锡)", "印刷"),
        ("DEMO-D-SOLDER-INSUFFICIENT", "少锡 / 缺锡", "印刷"),
        ("DEMO-D-MOUNT-MISS", "缺件", "贴片"),
        ("DEMO-D-MOUNT-SHIFT", "贴片偏移", "贴片"),
        ("DEMO-D-MOUNT-TOMBSTONE", "立碑", "贴片"),
        ("DEMO-D-REFLOW-COLD", "冷焊", "回流焊"),
        ("DEMO-D-REFLOW-POROSITY", "焊点气孔", "回流焊"),
        ("DEMO-D-AOI-FLUX", "助焊剂残留", "AOI"),
        ("DEMO-D-AOI-SCRATCH", "板面划伤", "AOI"),
        ("DEMO-D-FT-SHORT", "电气短路", "功能测试"),
        ("DEMO-D-FT-OPEN", "电气开路", "功能测试"),
        ("DEMO-D-FT-POWER", "功耗超标", "功能测试"),
    ];

    /// <summary>BOM 行:物料编码 → 单台用量 / 损耗率(每个产品一套)。</summary>
    private static readonly (string MaterialCode, decimal Quantity, decimal LossRate)[][] BomSeeds =
    [
        [
            ("DEMO-M-PCB", 1m, 0.01m), ("DEMO-M-SOLDER", 1.2m, 0.05m), ("DEMO-M-CAP", 24m, 0.02m),
            ("DEMO-M-RES", 16m, 0.02m), ("DEMO-M-IC", 1m, 0.005m), ("DEMO-M-SHIELD", 1m, 0.01m),
            ("DEMO-M-CASE", 1m, 0.01m),
        ],
        [
            ("DEMO-M-PCB", 1m, 0.01m), ("DEMO-M-SOLDER", 0.9m, 0.05m), ("DEMO-M-CAP", 18m, 0.02m),
            ("DEMO-M-RES", 12m, 0.02m), ("DEMO-M-IC", 1m, 0.005m), ("DEMO-M-SHIELD", 1m, 0.01m),
        ],
        [
            ("DEMO-M-PCB", 1m, 0.01m), ("DEMO-M-SOLDER", 0.8m, 0.05m), ("DEMO-M-CAP", 20m, 0.02m),
            ("DEMO-M-RES", 14m, 0.02m), ("DEMO-M-IC", 1m, 0.005m), ("DEMO-M-CASE", 1m, 0.01m),
        ],
    ];

    private static readonly string[] SupplierNames =
        ["昆山华新电子", "苏州日盛精密", "深圳中芯微", "东莞立讯材料", "上海千硕电子"];

    private static readonly string[] DownReasonCodes =
        ["DEMO-DOWN-MECHANIC", "DEMO-DOWN-ELECTRIC", "DEMO-DOWN-MATERIAL", "DEMO-DOWN-PROGRAM"];

    // ============================================================
    // 入口
    // ============================================================

    /// <summary>先清理旧演示数据,再重建一批新的(幂等,可反复调用)。</summary>
    public async Task<DemoSeedSummary> ResetAsync(DemoSeedOptions options, CancellationToken cancellationToken = default)
    {
        if (options.Days is < 1 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Days 必须在 1~180 之间");
        }
        if (options.WorkOrders is < 4 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "WorkOrders 必须在 4~60 之间");
        }
        if (options.SnPerOrder is < 10 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SnPerOrder 必须在 10~500 之间");
        }

        // 重置流程里紧跟着就会重新播种 + 重算一遍,这里就不必白跑一次预聚合重算了
        var removed = await CleanupAsync(options.Days, rebuildMetrics: false, cancellationToken);
        logger.LogInformation("演示数据清理完成,删除 {Removed} 行", removed);

        var state = await SeedMasterDataAsync(options, cancellationToken);
        await SeedEquipmentsAsync(state, cancellationToken);
        var lotCount = await SeedMaterialLotsAsync(state, cancellationToken);
        var productionCounts = await SeedProductionAsync(options, state, cancellationToken);
        var qualityCounts = await SeedQualityAsync(options, state, cancellationToken);
        var andonCount = await SeedAndonAsync(state, cancellationToken);

        await SpreadTimelineAsync(options, cancellationToken);

        var metricShifts = await metricsAggregator.RebuildAsync(
            DateOnly.FromDateTime(TodayUtc).AddDays(-(options.Days - 1)),
            DateOnly.FromDateTime(TodayUtc),
            null,
            cancellationToken);

        logger.LogInformation(
            "演示数据生成完成:工单 {WorkOrders} / SN {Sn} / 过站 {Trackings} / 检验 {Inspections} / 预聚合班次 {Shifts}",
            productionCounts.WorkOrders, productionCounts.SerialNumbers, productionCounts.WipTrackings,
            qualityCounts.Inspections, metricShifts);

        return new DemoSeedSummary(
            removed,
            state.Shifts,
            state.CalendarDays,
            state.WorkCenters,
            state.Products.Count,
            state.MaterialsByName.Count,
            state.OperationsInOrder.Count,
            state.Boms,
            state.Routings,
            state.DefectCodes,
            state.Equipments.Count,
            lotCount,
            productionCounts.WorkOrders,
            productionCounts.WorkOrderOperations,
            productionCounts.SerialNumbers,
            productionCounts.WipTrackings,
            productionCounts.ProductionReports,
            qualityCounts.Inspections,
            qualityCounts.Nonconformances,
            andonCount,
            metricShifts,
            "演示数据已就绪。工单管理 / SN 过站 / 质量管理 / 报表与班次 / 设备与 Andon / 车间大屏均已可见数据;"
            + "预聚合汇总表已按最近 " + options.Days + " 天重算完毕。");
    }

    /// <summary>只读:统计当前库里的演示数据条数。</summary>
    public async Task<Dictionary<string, long>> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, long>();
        var statements = new (string Key, string Sql)[]
        {
            ("shifts", "SELECT count(*) FROM reporting.shifts WHERE \"Code\" LIKE 'DEMO-%'"),
            ("workCenters", "SELECT count(*) FROM masterdata.work_centers WHERE \"Code\" LIKE 'DEMO-%'"),
            ("products", "SELECT count(*) FROM masterdata.products WHERE \"Code\" LIKE 'DEMO-%'"),
            ("materials", "SELECT count(*) FROM masterdata.materials WHERE \"Code\" LIKE 'DEMO-%'"),
            ("equipments", "SELECT count(*) FROM equipment.equipments WHERE \"Code\" LIKE 'DEMO-%'"),
            ("materialLots", "SELECT count(*) FROM quality.material_lots WHERE \"MaterialCode\" LIKE 'DEMO-%'"),
            ("workOrders", "SELECT count(*) FROM production.work_orders WHERE \"ProductCode\" LIKE 'DEMO-%'"),
            ("serialNumbers", "SELECT count(*) FROM production.serial_numbers WHERE \"ProductCode\" LIKE 'DEMO-%'"),
            ("wipTrackings", "SELECT count(*) FROM production.wip_trackings WHERE \"WorkOrderId\" IN (SELECT \"Id\" FROM production.work_orders WHERE \"ProductCode\" LIKE 'DEMO-%')"),
            ("productionReports", "SELECT count(*) FROM production.production_reports WHERE \"WorkOrderId\" IN (SELECT \"Id\" FROM production.work_orders WHERE \"ProductCode\" LIKE 'DEMO-%')"),
            ("inspections", "SELECT count(*) FROM quality.inspections WHERE \"InspectionNumber\" LIKE 'DEMO-%'"),
            ("nonconformances", "SELECT count(*) FROM quality.nonconformances WHERE \"NonconformanceNumber\" LIKE 'DEMO-%'"),
            ("andonCalls", "SELECT count(*) FROM equipment.andon_calls WHERE \"CallNumber\" LIKE 'DEMO-%'"),
        };

        foreach (var (key, sql) in statements)
        {
            result[key] = Convert.ToInt64(await ExecuteScalarAsync(sql, cancellationToken) ?? 0L);
        }

        return result;
    }

    // ============================================================
    // 清理
    // ============================================================

    /// <summary>
    /// 按依赖顺序硬删除全部演示数据。识别方式是各表自己的业务编码前缀(工单 / SN / 过站通过产品编码回推),
    /// 因此不会误伤手工录入的业务数据。
    /// </summary>
    /// <param name="days">演示数据铺开的天数(决定预聚合表要重算多长的窗口)。</param>
    /// <param name="rebuildMetrics">
    /// 是否在删除后重算预聚合表。默认开启——`reporting.daily_shift_metrics` 是纯派生数据,
    /// 删掉明细却不重算,这一段窗口的报表就会读到"演示期间的旧数字"。
    /// </param>
    public async Task<int> CleanupAsync(
        int days = 30,
        bool rebuildMetrics = true,
        CancellationToken cancellationToken = default)
    {
        string[] statements =
        [
            // ---------- 生产 ----------
            "DELETE FROM production.wip_trackings WHERE \"WorkOrderId\" IN (SELECT \"Id\" FROM production.work_orders WHERE \"ProductCode\" LIKE 'DEMO-%')",
            "DELETE FROM production.production_reports WHERE \"WorkOrderId\" IN (SELECT \"Id\" FROM production.work_orders WHERE \"ProductCode\" LIKE 'DEMO-%')",
            "DELETE FROM production.work_order_operations WHERE \"WorkOrderId\" IN (SELECT \"Id\" FROM production.work_orders WHERE \"ProductCode\" LIKE 'DEMO-%')",
            "DELETE FROM production.serial_numbers WHERE \"ProductCode\" LIKE 'DEMO-%'",
            "DELETE FROM production.work_orders WHERE \"ProductCode\" LIKE 'DEMO-%'",

            // ---------- 质量 ----------
            "DELETE FROM quality.repair_records WHERE \"NonconformanceId\" IN (SELECT \"Id\" FROM quality.nonconformances WHERE \"NonconformanceNumber\" LIKE 'DEMO-%')",
            "DELETE FROM quality.nonconformances WHERE \"NonconformanceNumber\" LIKE 'DEMO-%'",
            "DELETE FROM quality.inspection_items WHERE \"InspectionId\" IN (SELECT \"Id\" FROM quality.inspections WHERE \"InspectionNumber\" LIKE 'DEMO-%')",
            "DELETE FROM quality.inspections WHERE \"InspectionNumber\" LIKE 'DEMO-%'",
            "DELETE FROM quality.sn_material_consumptions WHERE \"MaterialCode\" LIKE 'DEMO-%'",
            "DELETE FROM quality.material_lots WHERE \"MaterialCode\" LIKE 'DEMO-%'",
            "DELETE FROM quality.defect_codes WHERE \"Code\" LIKE 'DEMO-%'",

            // ---------- 设备 ----------
            "DELETE FROM equipment.equipment_status_logs WHERE \"EquipmentId\" IN (SELECT \"Id\" FROM equipment.equipments WHERE \"Code\" LIKE 'DEMO-%')",
            "DELETE FROM equipment.equipment_maintenance_records WHERE \"EquipmentId\" IN (SELECT \"Id\" FROM equipment.equipments WHERE \"Code\" LIKE 'DEMO-%')",
            "DELETE FROM equipment.andon_calls WHERE \"CallNumber\" LIKE 'DEMO-%'",
            "DELETE FROM equipment.equipments WHERE \"Code\" LIKE 'DEMO-%'",

            // ---------- 主数据 ----------
            "DELETE FROM masterdata.bom_items WHERE \"BomId\" IN (SELECT \"Id\" FROM masterdata.boms WHERE \"ProductId\" IN (SELECT \"Id\" FROM masterdata.products WHERE \"Code\" LIKE 'DEMO-%'))",
            "DELETE FROM masterdata.boms WHERE \"ProductId\" IN (SELECT \"Id\" FROM masterdata.products WHERE \"Code\" LIKE 'DEMO-%')",
            "DELETE FROM masterdata.routing_steps WHERE \"RoutingId\" IN (SELECT \"Id\" FROM masterdata.routings WHERE \"ProductId\" IN (SELECT \"Id\" FROM masterdata.products WHERE \"Code\" LIKE 'DEMO-%'))",
            "DELETE FROM masterdata.routings WHERE \"ProductId\" IN (SELECT \"Id\" FROM masterdata.products WHERE \"Code\" LIKE 'DEMO-%')",
            "DELETE FROM masterdata.products WHERE \"Code\" LIKE 'DEMO-%'",
            "DELETE FROM masterdata.materials WHERE \"Code\" LIKE 'DEMO-%'",
            "DELETE FROM masterdata.operations WHERE \"Code\" LIKE 'DEMO-%'",
            "DELETE FROM masterdata.work_centers WHERE \"Code\" LIKE 'DEMO-%'",

            // ---------- 报表配置 ----------
            "DELETE FROM reporting.shifts WHERE \"Code\" LIKE 'DEMO-%'",
            "DELETE FROM reporting.calendar_days WHERE \"Remark\" = '[DEMO] 演示日历'",
            // 预聚合表是派生数据、无法逐行打标:删掉演示窗口内的行,重建时会被重新算出
            $"DELETE FROM reporting.daily_shift_metrics WHERE \"ProductionDate\" >= CURRENT_DATE - {days}",
        ];

        var removed = 0;
        foreach (var statement in statements)
        {
            removed += await ExecuteAsync(statement, cancellationToken);
        }

        // 明细删掉了,派生数据必须跟着重算,否则这一段窗口的报表会读到"演示期间的旧数字"
        if (rebuildMetrics)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            await metricsAggregator.RebuildAsync(today.AddDays(-(Math.Max(days, 1) - 1)), today, null, cancellationToken);
        }

        return removed;
    }

    // ============================================================
    // 主数据:班次 / 日历 / 工作中心 / 产品 / 物料 / 工序 / 不良代码 / BOM / 工艺路线
    // ============================================================

    private async Task<SeedState> SeedMasterDataAsync(DemoSeedOptions options, CancellationToken cancellationToken)
    {
        var state = new SeedState();

        // ---------- 班次:白班 + 跨天夜班(用于验证"夜班归属开始日"的口径) ----------
        reporting.Shifts.Add(new ShiftDefinition(
            "DEMO-DAY", "白班 08:30-20:30", new TimeOnly(8, 30), new TimeOnly(20, 30), null, 1, "[DEMO] 演示班次"));
        reporting.Shifts.Add(new ShiftDefinition(
            "DEMO-NIGHT", "夜班 20:30-08:30", new TimeOnly(20, 30), new TimeOnly(8, 30), null, 2, "[DEMO] 演示班次"));
        state.Shifts = 2;

        // ---------- 生产日历:窗口内的周日设为非生产日 ----------
        var today = DateOnly.FromDateTime(TodayUtc);
        for (var offset = 0; offset < options.Days; offset++)
        {
            var date = today.AddDays(-offset);
            if (date.DayOfWeek != DayOfWeek.Sunday)
            {
                continue;
            }

            if (await reporting.CalendarDays.AnyAsync(d => d.Date == date, cancellationToken))
            {
                continue;
            }

            reporting.CalendarDays.Add(new CalendarDay(date, false, "周日休息", "[DEMO] 演示日历"));
            state.CalendarDays++;
        }

        // ---------- 工作中心:3 条产线 + 每线 5 个机台工位 ----------
        for (var lineIndex = 0; lineIndex < 3; lineIndex++)
        {
            var lineCode = $"DEMO-SMT-{lineIndex + 1:D2}";
            var line = new WorkCenter(lineCode, $"SMT {lineIndex + 1} 线", WorkCenterType.Line, LineWorkshop, "[DEMO] 演示产线");
            masterData.WorkCenters.Add(line);
            state.Lines.Add(line);
            state.WorkCenters++;

            foreach (var machine in MachineSeeds)
            {
                var station = new WorkCenter(
                    $"{lineCode}-{machine.Suffix}",
                    $"{machine.StationName}({lineIndex + 1} 线)",
                    WorkCenterType.Station,
                    LineWorkshop,
                    "[DEMO] 演示工位");
                station.UpdateLayout(WorkCenterType.Station, LineWorkshop, line.Id);
                masterData.WorkCenters.Add(station);
                state.WorkCenters++;
                state.Stations[(lineIndex, machine.Suffix)] = station;
            }
        }

        // ---------- 产品 / 物料 / 工序 ----------
        foreach (var seed in ProductSeeds)
        {
            var product = new Product(seed.Code, seed.Name, seed.Spec, "PCS", "[DEMO] 演示产品");
            masterData.Products.Add(product);
            state.Products.Add(product);
        }

        foreach (var seed in MaterialSeeds)
        {
            var material = new Material(seed.Code, seed.Name, seed.Type, seed.Spec, seed.Unit, "[DEMO] 演示物料");
            masterData.Materials.Add(material);
            state.MaterialsByName[seed.Code] = material;
        }

        for (var index = 0; index < OperationSeeds.Length; index++)
        {
            var seed = OperationSeeds[index];
            var operation = new Operation(
                seed.Code,
                seed.Name,
                seed.Seconds,
                seed.Key,
                state.Stations[(0, MachineSeeds[index].Suffix)].Id,
                "[DEMO] 演示工序");
            masterData.Operations.Add(operation);
            state.OperationsInOrder.Add(operation);
            state.OperationsByCode[seed.Code] = operation;
        }

        // ---------- 不良代码(SMT 分工序预置) ----------
        foreach (var seed in DefectSeeds)
        {
            quality.DefectCodes.Add(new DefectCode(seed.Code, seed.Name, seed.Category, "[DEMO] 演示不良代码"));
            state.DefectCodes++;
        }

        // ---------- BOM + 工艺路线(每个产品一套,V1.0 并激活) ----------
        for (var productIndex = 0; productIndex < state.Products.Count; productIndex++)
        {
            var product = state.Products[productIndex];

            var bom = new Bom(product.Id, "V1.0", "[DEMO] 演示 BOM");
            foreach (var (materialCode, quantity, lossRate) in BomSeeds[productIndex])
            {
                var material = state.MaterialsByName[materialCode];
                bom.AddItem(material.Id, quantity, material.Unit, lossRate);
            }
            bom.Activate();
            masterData.Boms.Add(bom);
            state.Boms++;

            var routing = new Routing(product.Id, "V1.0", "[DEMO] 演示工艺路线");
            var steps = new List<RoutingStepSpec>();
            for (var stepIndex = 0; stepIndex < OperationSeeds.Length; stepIndex++)
            {
                var operation = state.OperationsInOrder[stepIndex];
                var station = state.Stations[(productIndex % state.Lines.Count, MachineSeeds[stepIndex].Suffix)];
                steps.Add(new RoutingStepSpec(
                    (stepIndex + 1) * 10,
                    operation.Id,
                    station.Id,
                    operation.StandardSeconds,
                    operation.IsKeyOperation));
            }
            routing.ReplaceSteps(steps);
            routing.Activate();
            masterData.Routings.Add(routing);
            state.Routings++;

            state.Recipes.Add(new Recipe(product, bom, routing));
        }

        await masterData.SaveChangesAsync(cancellationToken);
        await quality.SaveChangesAsync(cancellationToken);

        return state;
    }

    // ============================================================
    // 设备台账 + 当前状态 + 历史停机段 + 点检记录
    // ============================================================

    private async Task SeedEquipmentsAsync(SeedState state, CancellationToken cancellationToken)
    {
        var today = TodayUtc;
        var statusLogs = new List<EquipmentStatusLog>();
        var maintenanceRecords = new List<EquipmentMaintenanceRecord>();

        for (var lineIndex = 0; lineIndex < state.Lines.Count; lineIndex++)
        {
            var line = state.Lines[lineIndex];

            for (var machineIndex = 0; machineIndex < MachineSeeds.Length; machineIndex++)
            {
                var machine = MachineSeeds[machineIndex];
                var code = $"{line.Code}-{machine.Suffix}";
                var entity = new EquipmentEntity(
                    code,
                    $"{machine.MachineName} {lineIndex + 1}#",
                    machine.Model,
                    $"{code}-SN{Random.Next(10000, 99999)}",
                    state.Stations[(lineIndex, machine.Suffix)].Id,
                    line.Name,
                    "[DEMO] 演示设备");
                equipment.Equipments.Add(entity);
                state.Equipments.Add(entity);
                state.EquipmentByCode[code] = entity;

                // 最近 N 天的停机历史:直接构造日志并保持"成对出现",停机时长才能被配对计算
                for (var stop = 1; stop <= 4; stop++)
                {
                    var downAt = today
                        .AddDays(-Random.Next(1, 25))
                        .AddHours(Random.Next(0, 24))
                        .AddMinutes(Random.Next(0, 60));
                    var durationMinutes = Random.Next(15, 180);
                    var reasonCode = DownReasonCodes[Random.Next(DownReasonCodes.Length)];

                    statusLogs.Add(new EquipmentStatusLog(
                        entity.Id, EquipmentStatus.Running, EquipmentStatus.Down,
                        reasonCode, DownReasonText(reasonCode), null, downAt));
                    statusLogs.Add(new EquipmentStatusLog(
                        entity.Id, EquipmentStatus.Down, EquipmentStatus.Running,
                        null, null, null, downAt.AddMinutes(durationMinutes)));
                }

                // 点检记录
                for (var day = 1; day <= 3; day++)
                {
                    var abnormal = Random.NextDouble() < 0.2;
                    maintenanceRecords.Add(new EquipmentMaintenanceRecord(
                        entity.Id,
                        EquipmentMaintenanceType.DailyCheck,
                        $"{machine.MachineName} 日常点检(气压 / 轨道 / 吸嘴 / 真空)",
                        abnormal ? EquipmentMaintenanceResult.Abnormal : EquipmentMaintenanceResult.Normal,
                        abnormal ? "吸嘴轻微堵塞,已清洁复位" : null,
                        null,
                        today.AddDays(-Random.Next(1, 20)).AddHours(Random.Next(6, 10))));
                }
            }
        }

        // 当前状态:一 / 二线运行,二线贴片机待机,三线回流炉故障(带停机原因码)
        foreach (var entity in state.Equipments)
        {
            var target = EquipmentStatus.Running;
            string? reasonCode = null;
            string? reason = null;

            if (entity.Code == $"{state.Lines[2].Code}-RF")
            {
                target = EquipmentStatus.Down;
                reasonCode = "DEMO-DOWN-HEAT";
                reason = "回流炉温区加热器报警,等待备件";
            }
            else if (entity.Code == $"{state.Lines[1].Code}-MT")
            {
                target = EquipmentStatus.Idle;
                reason = "等待工单切换";
            }

            var log = entity.ChangeStatus(target, reasonCode, reason, null);
            if (log is not null)
            {
                statusLogs.Add(log);
            }
        }

        equipment.StatusLogs.AddRange(statusLogs);
        equipment.MaintenanceRecords.AddRange(maintenanceRecords);
        await equipment.SaveChangesAsync(cancellationToken);
    }

    private static string DownReasonText(string code) => code switch
    {
        "DEMO-DOWN-MECHANIC" => "机械故障:传送轨道卡料",
        "DEMO-DOWN-ELECTRIC" => "电气故障:伺服驱动器过流",
        "DEMO-DOWN-MATERIAL" => "缺料停机:等待物料补给",
        "DEMO-DOWN-PROGRAM" => "程序异常:贴装程序参数缺失",
        _ => "停机",
    };

    // ============================================================
    // 来料批次 + IQC(判定结果回写批次准入)
    // ============================================================

    private async Task<int> SeedMaterialLotsAsync(SeedState state, CancellationToken cancellationToken)
    {
        var lots = new List<MaterialLot>();
        var index = 0;

        foreach (var materialSeed in MaterialSeeds)
        {
            index++;
            var material = state.MaterialsByName[materialSeed.Code];
            var lot = new MaterialLot(
                $"{Prefix}LOT-{materialSeed.Code.Replace("DEMO-M-", string.Empty, StringComparison.Ordinal)}-{TodayUtc:yyyyMMdd}-{index:D2}",
                material.Code,
                LotQuantity(materialSeed.Code),
                material.Name,
                SupplierNames[index % SupplierNames.Length],
                $"SUP-{Random.Next(100000, 999999)}",
                material.Unit,
                TodayUtc.AddDays(-Random.Next(1, 20)),
                "[DEMO] 演示来料批次");
            quality.MaterialLots.Add(lot);
            state.LotsByMaterial[material.Code] = lot;
            state.LotsInOrder.Add(lot);
            lots.Add(lot);
        }

        await quality.SaveChangesAsync(cancellationToken);

        // IQC:每批一张单;第 3 个批次判不合格(用来演示 NCR 派生与批次拒收)
        var inspections = new List<Inspection>();
        for (var lotIndex = 0; lotIndex < lots.Count; lotIndex++)
        {
            var lot = lots[lotIndex];
            var materialName = state.MaterialsByName[lot.MaterialCode].Name;
            var passed = lotIndex != 2;

            var inspection = BuildInspection(
                $"{Prefix}IQC-{lotIndex + 1:D5}",
                InspectionType.Iqc,
                sampleSize: 20,
                workOrderId: null,
                operationId: null,
                sn: null,
                productCode: state.Products[lotIndex % state.Products.Count].Code,
                lotNumber: lot.LotNumber,
                materialCode: lot.MaterialCode,
                aqlLevel: "AQL 1.0",
                acceptedLimit: 0,
                rejectedLimit: 1,
                points:
                [
                    new InspectPoint("外观", "无氧化 / 无变形 / 无破损", null, null, true,
                        passed ? "合格" : "不合格", null, passed, passed ? null : "DEMO-D-AOI-SCRATCH"),
                    new InspectPoint("尺寸偏差(mm)", "-0.05 ~ +0.05", -0.05m, 0.05m, false,
                        null, passed ? RandomAround(0m, 0.03m) : 0.09m, null, null),
                ],
                defectQuantity: passed ? 0 : 3,
                concession: false,
                inspectorName: "演示检验员",
                remark: $"[DEMO] {materialName} 来料检验");

            inspections.Add(inspection);
            lot.MarkInspected(
                passed,
                inspection.Id,
                inspection.InspectionNumber,
                passed ? "IQC 判定合格,允许投产" : "IQC 判定不合格,批次拒收");
        }

        quality.Inspections.AddRange(inspections);
        await quality.SaveChangesAsync(cancellationToken);

        logger.LogInformation("来料批次 {Lots} 个、IQC 检验单 {Inspections} 张已生成", lots.Count, inspections.Count);
        return lots.Count;
    }

    // ============================================================
    // 生产:工单 → 下达 → 报工 → SN → 过站
    // ============================================================

    private async Task<ProductionCounts> SeedProductionAsync(
        DemoSeedOptions options,
        SeedState state,
        CancellationToken cancellationToken)
    {
        var counts = new ProductionCounts();
        var total = options.WorkOrders;
        var completedIndex = total - 1;
        var draftCount = Math.Max(2, total / 4);
        var releasedCount = Math.Max(1, total / 5);

        for (var index = 0; index < total; index++)
        {
            var recipe = state.Recipes[index % state.Recipes.Count];
            var line = state.Lines[index % state.Lines.Count];
            var plannedQuantity = 120 + Random.Next(0, 8) * 20;

            var orderNumber = await NextOrderNumberAsync(cancellationToken);
            var workOrder = new WorkOrder(
                orderNumber,
                recipe.Product.Id,
                recipe.Product.Code,
                recipe.Product.Name,
                plannedQuantity,
                TodayUtc.AddDays(-Random.Next(1, 12)),
                TodayUtc.AddDays(Random.Next(2, 10)),
                line.Name,
                OrderRemark);

            production.WorkOrders.Add(workOrder);
            await production.SaveChangesAsync(cancellationToken);
            counts.WorkOrders++;

            // 草稿工单到此为止(演示"未下达"分支)
            if (index < draftCount)
            {
                state.WorkOrders.Add(new SeededWorkOrder(workOrder, [], WorkOrderStatus.Draft, 0));
                continue;
            }

            // 下达:按生效工艺路线展开工序任务,并快照 BOM / 工艺路线版本
            var snapshot = new RoutingReleaseSnapshot(
                recipe.Routing.Id,
                recipe.Routing.Version,
                recipe.Bom.Id,
                recipe.Bom.Version,
                recipe.Routing.OrderedSteps
                    .Select(step =>
                    {
                        var operation = state.OperationsInOrder.First(o => o.Id == step.OperationId);
                        return new RoutingStepReleaseSnapshot(
                            step.Sequence, step.OperationId, operation.Code, operation.Name,
                            step.WorkCenterId, step.StandardSeconds, step.IsQualityGate);
                    })
                    .ToList());

            Ensure(workOrder.Release(snapshot), $"下达工单 {orderNumber}");
            foreach (var operation in workOrder.Operations)
            {
                production.WorkOrderOperations.Add(operation);
                counts.WorkOrderOperations++;
            }
            await production.SaveChangesAsync(cancellationToken);

            // 进度:已下达 = 0 道报工;最后一单全部完成;其余按序号递增
            var operations = workOrder.OrderedOperations;
            int completedOperations;
            bool partialNext;

            if (index == completedIndex)
            {
                completedOperations = operations.Count;
                partialNext = false;
            }
            else if (index < draftCount + releasedCount)
            {
                completedOperations = 0;
                partialNext = false;
            }
            else
            {
                completedOperations = Math.Clamp(index - draftCount - releasedCount + 1, 1, operations.Count - 1);
                partialNext = Random.NextDouble() < 0.7;
            }

            if (completedOperations > 0 || partialNext)
            {
                Ensure(workOrder.StartProduction(), $"开工工单 {orderNumber}");
            }

            for (var operationIndex = 0; operationIndex < completedOperations; operationIndex++)
            {
                var operation = operations[operationIndex];
                var defect = (int)Math.Round(plannedQuantity * 0.02);
                var scrap = (int)Math.Round(plannedQuantity * 0.01);
                var good = plannedQuantity - defect - scrap;
                var workedSeconds = (good + defect + scrap) * operation.StandardSeconds;

                Ensure(
                    workOrder.ReportOperation(operation.Id, good, defect, scrap, workedSeconds),
                    $"报工 {orderNumber} / {operation.OperationName}");

                production.ProductionReports.Add(workOrder.CreateReport(
                    operation.Id, good, defect, scrap,
                    DefectCodeForOperation(operationIndex),
                    currentUser.UserId,
                    EquipmentOf(state, line, operationIndex)?.Id,
                    workedSeconds,
                    ProductionReportType.Normal,
                    "[DEMO] 演示报工"));
                counts.ProductionReports++;
            }

            if (partialNext && completedOperations < operations.Count)
            {
                var operation = operations[completedOperations];
                var defect = Math.Max(1, (int)Math.Round(plannedQuantity * 0.01));
                var good = Math.Max(1, (int)Math.Round(plannedQuantity * 0.55));
                var workedSeconds = (good + defect) * operation.StandardSeconds;

                Ensure(
                    workOrder.ReportOperation(operation.Id, good, defect, 0, workedSeconds),
                    $"部分报工 {orderNumber} / {operation.OperationName}");

                production.ProductionReports.Add(workOrder.CreateReport(
                    operation.Id, good, defect, 0,
                    DefectCodeForOperation(completedOperations),
                    currentUser.UserId,
                    null,
                    workedSeconds,
                    ProductionReportType.Normal,
                    "[DEMO] 演示报工"));
                counts.ProductionReports++;
            }

            await production.SaveChangesAsync(cancellationToken);
            state.WorkOrders.Add(new SeededWorkOrder(workOrder, operations, workOrder.Status, completedOperations));

            // ---------- SN 与过站 ----------
            var generated = await SeedSerialNumbersAsync(
                options, state, workOrder, recipe, operations, completedOperations, partialNext, cancellationToken);
            counts.SerialNumbers += generated.SerialNumbers;
            counts.WipTrackings += generated.WipTrackings;
        }

        return counts;
    }

    private static EquipmentEntity? EquipmentOf(SeedState state, WorkCenter line, int operationIndex)
        => state.EquipmentByCode.TryGetValue($"{line.Code}-{MachineSeeds[operationIndex].Suffix}", out var machine)
            ? machine
            : null;

    private static string? DefectCodeForOperation(int operationIndex) => operationIndex switch
    {
        0 => "DEMO-D-SOLDER-BRIDGE",
        1 => "DEMO-D-MOUNT-MISS",
        2 => "DEMO-D-REFLOW-COLD",
        3 => "DEMO-D-AOI-FLUX",
        4 => "DEMO-D-FT-SHORT",
        _ => null,
    };

    /// <summary>与生产环境一致:按生产日原子取号(<c>INSERT ... ON CONFLICT ... RETURNING</c>)。</summary>
    private async Task<string> NextOrderNumberAsync(CancellationToken cancellationToken)
    {
        var sequenceDate = DateOnly.FromDateTime(DateTime.Now);
        await using var command = CreateCommand(
            """
            INSERT INTO production.work_order_daily_sequences (sequence_date, last_value, updated_at)
            VALUES (@sequence_date, 1, now())
            ON CONFLICT (sequence_date)
            DO UPDATE SET last_value = production.work_order_daily_sequences.last_value + 1,
                          updated_at = now()
            RETURNING last_value;
            """);
        AddParameter(command, "sequence_date", sequenceDate);
        var value = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return $"WO-{sequenceDate:yyyyMMdd}-{value:D4}";
    }

    /// <summary>
    /// 为一张工单生成 SN 与完整过站轨迹:已报工的工序走一遍「进站 → 出站」,
    /// 少量 SN 在 AOI 判不合格(停在当前工序)、少量报废,让良率与不良 Pareto 有真实分布。
    /// </summary>
    private async Task<ProductionCounts> SeedSerialNumbersAsync(
        DemoSeedOptions options,
        SeedState state,
        WorkOrder workOrder,
        Recipe recipe,
        IReadOnlyList<WorkOrderOperation> operations,
        int completedOperations,
        bool partialNext,
        CancellationToken cancellationToken)
    {
        var counts = new ProductionCounts();

        for (var index = 1; index <= options.SnPerOrder; index++)
        {
            production.SerialNumbers.Add(new SerialNumber(
                $"{workOrder.OrderNumber}-{index:D4}", workOrder.Id, recipe.Product.Id, recipe.Product.Code));
        }
        await production.SaveChangesAsync(cancellationToken);

        var serials = await production.SerialNumbers
            .Where(s => s.WorkOrderId == workOrder.Id)
            .OrderBy(s => s.Sn)
            .ToListAsync(cancellationToken);
        counts.SerialNumbers = serials.Count;

        var trackings = new List<WipTracking>();
        var consumptions = new List<SnMaterialConsumption>();
        var isCompletedOrder = workOrder.Status == WorkOrderStatus.Completed;

        foreach (var serial in serials)
        {
            var reachable = completedOperations;
            if (partialNext && Random.NextDouble() < 0.7)
            {
                reachable = Math.Min(completedOperations + 1, operations.Count);
            }

            var failAtAoi = operations.Count > 3 && Random.NextDouble() < 0.05;
            var scrap = Random.NextDouble() < 0.02;
            var lastOperationIndex = operations.Count - 1;

            for (var operationIndex = 0; operationIndex < reachable; operationIndex++)
            {
                var operation = operations[operationIndex];
                var machine = EquipmentOf(state, state.Lines[operationIndex % state.Lines.Count], operationIndex);

                var trackedIn = serial.TrackIn(operation.Id, operation.OperationName, currentUser.UserId, machine?.Id);
                if (trackedIn.IsFailure)
                {
                    break;
                }
                trackings.Add(trackedIn.Value);

                if (failAtAoi && operation.OperationName.Contains("AOI", StringComparison.Ordinal))
                {
                    var failed = serial.TrackOut(
                        operation.Id, operation.OperationName, WipResult.Fail, false,
                        currentUser.UserId, machine?.Id, "[DEMO] AOI 判不合格,停留本工序");
                    if (failed.IsSuccess)
                    {
                        trackings.Add(failed.Value);
                    }
                    break;
                }

                // 只有"工单已完工"的最后一颗才整颗完工;否则停在最后一道工序
                var isFinalPass = operationIndex == lastOperationIndex && isCompletedOrder;
                var passed = serial.TrackOut(
                    operation.Id, operation.OperationName, WipResult.Pass, isFinalPass,
                    currentUser.UserId, machine?.Id);
                if (passed.IsSuccess)
                {
                    trackings.Add(passed.Value);
                }
            }

            if (scrap && serial.Status == SerialNumberStatus.InProcess)
            {
                serial.Scrap("[DEMO] 演示报废");
            }

            // 绑定上游来料批次,形成「SN ← 批次」的正向谱系
            BindConsumption(consumptions, state, serial, workOrder, operations, "DEMO-M-PCB", 0);
            BindConsumption(consumptions, state, serial, workOrder, operations, "DEMO-M-IC", 1);
        }

        production.WipTrackings.AddRange(trackings);
        quality.SnMaterialConsumptions.AddRange(consumptions);
        await production.SaveChangesAsync(cancellationToken);
        await quality.SaveChangesAsync(cancellationToken);

        counts.WipTrackings = trackings.Count;
        state.SerialNumbersByWorkOrder[workOrder.Id] = serials;
        return counts;
    }

    private void BindConsumption(
        List<SnMaterialConsumption> consumptions,
        SeedState state,
        SerialNumber serial,
        WorkOrder workOrder,
        IReadOnlyList<WorkOrderOperation> operations,
        string materialCode,
        int operationIndex)
    {
        if (!state.LotsByMaterial.TryGetValue(materialCode, out var lot) || !lot.CanConsume)
        {
            return;
        }

        if (lot.Consume(1m).IsFailure)
        {
            return;
        }

        var operation = operationIndex < operations.Count ? operations[operationIndex] : null;
        consumptions.Add(new SnMaterialConsumption(
            serial.Sn,
            lot.MaterialCode,
            lot.LotNumber,
            1m,
            workOrder.Id,
            operation?.Id,
            operation?.OperationName,
            null,
            currentUser.UserId,
            "[DEMO] 演示用料"));
    }

    // ============================================================
    // 质量:IPQC / FQC 检验 + 不合格处置
    // ============================================================

    private async Task<QualityCounts> SeedQualityAsync(
        DemoSeedOptions options,
        SeedState state,
        CancellationToken cancellationToken)
    {
        var counts = new QualityCounts();
        var inspectorId = currentUser.UserId;
        var inspections = new List<Inspection>();

        var releasedOrders = state.WorkOrders.Where(w => w.Status != WorkOrderStatus.Draft).ToList();
        var iqcCount = await quality.Inspections.CountAsync(i => i.InspectionNumber.StartsWith(Prefix + "IQC-"), cancellationToken);

        // ---------- IPQC:按天铺开,给 SPC 趋势提供 30+ 个点 ----------
        if (releasedOrders.Count > 0)
        {
            var ipqcCount = Math.Max(30, options.Days);
            for (var index = 0; index < ipqcCount; index++)
            {
                var seeded = releasedOrders[index % releasedOrders.Count];
                var aoiOperation = seeded.Operations.FirstOrDefault(o => o.OperationName.Contains("AOI", StringComparison.Ordinal));
                var serials = state.SerialNumbersByWorkOrder.TryGetValue(seeded.WorkOrder.Id, out var list) ? list : [];

                // 焊点高度围绕 0.10mm 波动,每 11 个点制造一次"超出控制限",让 SPC 能报出判异
                var outOfControl = index % 11 == 5;
                var solderHeight = outOfControl
                    ? 0.132m
                    : Math.Round(0.095m + (decimal)Random.NextDouble() * 0.012m, 3);
                var pasteThickness = Math.Round(0.115m + (decimal)Random.NextDouble() * 0.025m, 3);

                var inspection = new Inspection(
                    $"{Prefix}IPQC-{index + 1:D5}",
                    InspectionType.Ipqc,
                    sampleSize: 5,
                    workOrderId: seeded.WorkOrder.Id,
                    workOrderOperationId: aoiOperation?.Id,
                    sn: serials.Count > 0 ? serials[index % serials.Count].Sn : null,
                    materialId: null,
                    materialCode: null,
                    lotNumber: null,
                    productCode: seeded.WorkOrder.ProductCode,
                    aqlLevel: "AQL 1.0",
                    acceptedLimit: 0,
                    rejectedLimit: 1);

                var heightItem = inspection.AddItem("焊点高度(mm)", "0.08 ~ 0.12", 0.08m, 0.12m, true);
                var pasteItem = inspection.AddItem("锡膏厚度(mm)", "0.10 ~ 0.16", 0.10m, 0.16m, false);
                var lookItem = inspection.AddItem("外观", "无残留 / 无连锡", null, null, false);

                Ensure(
                    inspection.RecordItem(heightItem.Id, solderHeight.ToString(CultureInfo.InvariantCulture), solderHeight,
                        null, outOfControl ? "DEMO-D-REFLOW-POROSITY" : null),
                    "IPQC 焊点高度录入");
                Ensure(
                    inspection.RecordItem(pasteItem.Id, pasteThickness.ToString(CultureInfo.InvariantCulture), pasteThickness, null, null),
                    "IPQC 锡膏厚度录入");
                Ensure(
                    inspection.RecordItem(lookItem.Id, outOfControl ? "不合格" : "合格", null, !outOfControl,
                        outOfControl ? "DEMO-D-AOI-FLUX" : null),
                    "IPQC 外观录入");
                Ensure(
                    inspection.Submit(outOfControl ? 1 : 0, inspectorId, "演示检验员", false, "[DEMO] 过程检验"),
                    "IPQC 判定");

                inspections.Add(inspection);
            }
        }

        // ---------- FQC:对已完工工单的已完工 SN 抽检 ----------
        foreach (var seeded in state.WorkOrders.Where(w => w.Status == WorkOrderStatus.Completed))
        {
            var serials = state.SerialNumbersByWorkOrder.TryGetValue(seeded.WorkOrder.Id, out var list) ? list : [];
            var completed = serials.Where(s => s.Status == SerialNumberStatus.Completed).Take(6).ToList();
            var sequence = 0;

            foreach (var serial in completed)
            {
                inspections.Add(BuildInspection(
                    $"{Prefix}FQC-{seeded.WorkOrder.OrderNumber}-{++sequence:D3}",
                    InspectionType.Fqc,
                    sampleSize: 3,
                    workOrderId: seeded.WorkOrder.Id,
                    operationId: seeded.Operations.Count > 0 ? seeded.Operations[^1].Id : null,
                    sn: serial.Sn,
                    productCode: seeded.WorkOrder.ProductCode,
                    lotNumber: null,
                    materialCode: null,
                    aqlLevel: "AQL 0.65",
                    acceptedLimit: 0,
                    rejectedLimit: 1,
                    points:
                    [
                        new InspectPoint("功放输出(dBm)", "18.5 ~ 19.5", 18.5m, 19.5m, true,
                            null, Math.Round(18.9m + (decimal)Random.NextDouble() * 0.4m, 2), null, null),
                        new InspectPoint("外观", "无划伤 / 无脏污", null, null, false, "合格", null, true, null),
                    ],
                    defectQuantity: 0,
                    concession: false,
                    inspectorName: "演示检验员",
                    remark: "[DEMO] 成品检验"));
            }
        }

        quality.Inspections.AddRange(inspections);
        await quality.SaveChangesAsync(cancellationToken);
        counts.Inspections = iqcCount + inspections.Count;

        // ---------- 不合格处置:不同状态各来一张,覆盖完整闭环 ----------
        var nonconformances = new List<Nonconformance>();
        var serialPool = releasedOrders
            .SelectMany(o => state.SerialNumbersByWorkOrder.TryGetValue(o.WorkOrder.Id, out var list) ? list : [])
            .Take(60)
            .ToList();

        var scenarios = new[]
        {
            (DispositionType.Rework, DispositionStatus.InProgress, "返工:重新贴装并过回流焊"),
            (DispositionType.Repair, DispositionStatus.PendingReinspect, "返修:补焊后待复检"),
            (DispositionType.Concession, DispositionStatus.Closed, "让步接收:客户特批放行"),
            (DispositionType.Scrap, DispositionStatus.Closed, "报废:无法修复"),
            (DispositionType.Return, DispositionStatus.Closed, "退货:整批退回供应商"),
        };

        for (var index = 0; index < scenarios.Length; index++)
        {
            var (disposition, expected, description) = scenarios[index];
            var serial = serialPool.Count > 0 ? serialPool[index % serialPool.Count] : null;
            var defect = DefectSeeds[index % DefectSeeds.Length];

            var nonconformance = new Nonconformance(
                $"{Prefix}NC-{index + 1:D5}",
                1 + Random.Next(0, 4),
                null,
                serial?.WorkOrderId,
                serial?.Sn,
                defect.Code,
                defect.Name,
                serial?.ProductCode ?? state.Products[0].Code);

            Ensure(
                nonconformance.Decide(
                    disposition,
                    needReinspect: expected == DispositionStatus.PendingReinspect,
                    handlerId: inspectorId,
                    remark: $"[DEMO] {description}"),
                "不合格处置决策");

            if (expected == DispositionStatus.PendingReinspect)
            {
                var repair = nonconformance.StartRepair("[DEMO] 更换元件并重新过炉", inspectorId, null);
                Ensure(repair, "开始维修");
                Ensure(
                    nonconformance.CompleteRepair(repair.Value.Id, "返修完成,外观已清洁", "[DEMO] 维修完成"),
                    "完成维修");
            }

            nonconformances.Add(nonconformance);
        }

        quality.Nonconformances.AddRange(nonconformances);
        quality.RepairRecords.AddRange(nonconformances.SelectMany(n => n.Repairs));
        await quality.SaveChangesAsync(cancellationToken);
        counts.Nonconformances = nonconformances.Count;

        return counts;
    }

    private static decimal RandomAround(decimal center, decimal amplitude)
        => Math.Round(center + (decimal)(Random.NextDouble() * 2 - 1) * amplitude, 3);

    /// <summary>构造一张"检验项全部录入并已判定"的检验单。</summary>
    private static Inspection BuildInspection(
        string number,
        InspectionType type,
        int sampleSize,
        Guid? workOrderId,
        Guid? operationId,
        string? sn,
        string? productCode,
        string? lotNumber,
        string? materialCode,
        string? aqlLevel,
        int acceptedLimit,
        int rejectedLimit,
        IReadOnlyList<InspectPoint> points,
        int defectQuantity,
        bool concession,
        string inspectorName,
        string remark)
    {
        var inspection = new Inspection(
            number, type, sampleSize, workOrderId, operationId, sn, null, materialCode, lotNumber,
            productCode, aqlLevel, acceptedLimit, rejectedLimit);

        foreach (var point in points)
        {
            var item = inspection.AddItem(point.Name, point.Standard, point.Low, point.High, point.Key);
            Ensure(
                inspection.RecordItem(
                    item.Id,
                    point.Text ?? point.Number?.ToString(CultureInfo.InvariantCulture),
                    point.Number,
                    point.Qualified,
                    point.DefectCode),
                $"录入检验项 {number} / {point.Name}");
        }

        Ensure(
            inspection.Submit(defectQuantity, null, inspectorName, concession, remark),
            $"判定检验单 {number}");
        return inspection;
    }

    /// <summary>检验项录入点:<see cref="Text"/> 用于定性项,<see cref="Number"/> 用于定量项。</summary>
    private sealed record InspectPoint(
        string Name,
        string? Standard,
        decimal? Low,
        decimal? High,
        bool Key,
        string? Text,
        decimal? Number,
        bool? Qualified,
        string? DefectCode);

    // ============================================================
    // Andon
    // ============================================================

    private async Task<int> SeedAndonAsync(SeedState state, CancellationToken cancellationToken)
    {
        if (state.Equipments.Count == 0)
        {
            return 0;
        }

        var operatorId = currentUser.UserId;
        var scenarios = new[]
        {
            (AndonType.EquipmentFailure, "回流炉温区报警,产品连续出现冷焊", 0),
            (AndonType.QualityIssue, "AOI 连续检出助焊剂残留,请质量工程师确认", 0),
            (AndonType.MaterialShortage, "0402 电容料盘即将用尽,请补料", 18),
            (AndonType.EquipmentFailure, "贴片机吸嘴堵塞停机", 12),
            (AndonType.Other, "换线等待贴装程序导入", 25),
            (AndonType.QualityIssue, "首件检验尺寸超差,请求复判", 0),
        };

        var calls = new List<AndonCall>();
        for (var index = 0; index < scenarios.Length; index++)
        {
            var (type, description, timeoutMinutes) = scenarios[index];
            var machine = state.Equipments[index % state.Equipments.Count];
            var call = new AndonCall(
                $"{Prefix}ANDON-{index + 1:D4}",
                type,
                description,
                AndonLevel.Yellow,
                machine.Id,
                machine.Code,
                machine.WorkCenterId,
                machine.LineName,
                null,
                timeoutMinutes == 0 ? 10 : timeoutMinutes,
                operatorId);

            switch (index % 5)
            {
                case 0:
                    // 超时未响应 → 升级为红灯
                    call.Escalate(DateTime.UtcNow.AddMinutes(20));
                    break;
                case 1:
                    Ensure(call.Respond(operatorId), "Andon 响应");
                    break;
                case 2:
                    Ensure(call.Respond(operatorId), "Andon 响应");
                    Ensure(call.Resolve("已补料,产线恢复"), "Andon 解决");
                    break;
                case 3:
                    Ensure(call.Respond(operatorId), "Andon 响应");
                    Ensure(call.Close("设备已修复并验证"), "Andon 关闭");
                    break;
                default:
                    // 保持"待响应"
                    break;
            }

            calls.Add(call);
        }

        equipment.AndonCalls.AddRange(calls);
        await equipment.SaveChangesAsync(cancellationToken);
        return calls.Count;
    }

    // ============================================================
    // 时间线铺开:把"现在"改成"最近 N 天",让报表 / SPC / OEE 有横轴
    // ============================================================

    private async Task SpreadTimelineAsync(DemoSeedOptions options, CancellationToken cancellationToken)
    {
        var days = options.Days.ToString(CultureInfo.InvariantCulture);
        // 天数取 days-1,再叠加 2~21 小时,保证所有行都落在 [今天-(days-1), 今天] 这段
        // 已被预聚合重算覆盖过的窗口内(不然会出现"有 SN 却没有对应指标行"的空洞)。
        var spanDays = $"GREATEST({days} - 1, 1)";

        // 同一个业务键两次求值结果一致(所以派生列能对齐),且落在 [2:00, 21:59] 之间,
        // 保证时间戳一定能被白班 / 夜班窗口覆盖到。
        string Spread(string key) =>
            $"now() - make_interval(days => (mod(abs(hashtext({key}::text)::bigint), {spanDays}))::int, "
            + $"hours => (mod(abs(hashtext(({key}::text) || 'h')::bigint), 20) + 2)::int, "
            + $"mins => (mod(abs(hashtext(({key}::text) || 'm')::bigint), 60))::int)";

        string[] statements =
        [
            // ---------- 主数据 ----------
            $"UPDATE masterdata.work_centers SET \"CreatedAt\" = {Spread("\"Code\"")} WHERE \"Code\" LIKE 'DEMO-%'",
            $"UPDATE masterdata.products SET \"CreatedAt\" = {Spread("\"Code\"")} WHERE \"Code\" LIKE 'DEMO-%'",
            $"UPDATE masterdata.materials SET \"CreatedAt\" = {Spread("\"Code\"")} WHERE \"Code\" LIKE 'DEMO-%'",
            $"UPDATE masterdata.operations SET \"CreatedAt\" = {Spread("\"Code\"")} WHERE \"Code\" LIKE 'DEMO-%'",
            $"UPDATE masterdata.boms SET \"CreatedAt\" = {Spread("\"Id\"")} WHERE \"ProductId\" IN (SELECT \"Id\" FROM masterdata.products WHERE \"Code\" LIKE 'DEMO-%')",
            $"UPDATE masterdata.routings SET \"CreatedAt\" = {Spread("\"Id\"")} WHERE \"ProductId\" IN (SELECT \"Id\" FROM masterdata.products WHERE \"Code\" LIKE 'DEMO-%')",
            $"UPDATE quality.defect_codes SET \"CreatedAt\" = {Spread("\"Code\"")} WHERE \"Code\" LIKE 'DEMO-%'",

            // ---------- 工单:创建时间决定「理论工时 / 实际工时」落在哪个班次窗口 ----------
            $"""
             UPDATE production.work_orders SET
                 "CreatedAt"    = {Spread("\"OrderNumber\"")},
                 "PlannedStart" = {Spread("\"OrderNumber\"")} - interval '1 day',
                 "PlannedEnd"   = {Spread("\"OrderNumber\"")} + interval '3 days',
                 "CompletedAt"  = CASE WHEN "Status" = 3 THEN {Spread("\"OrderNumber\"")} + interval '2 days' ELSE NULL END
             WHERE "ProductCode" LIKE 'DEMO-%'
             """,

            // ---------- SN:创建时间决定「产量 / 良率」落在哪个班次窗口 ----------
            $"""
             UPDATE production.serial_numbers SET
                 "CreatedAt"   = {Spread("\"Sn\"")},
                 "CompletedAt" = CASE WHEN "Status" = 1 THEN {Spread("\"Sn\"")} + interval '45 minutes' ELSE NULL END
             WHERE "ProductCode" LIKE 'DEMO-%'
             """,

            // 过站时刻 = SN 创建时刻 + 已流逝时间的一个比例(保证不晚于现在)
            """
            UPDATE production.wip_trackings t
            SET "TrackedAt" = s."CreatedAt" + (now() - s."CreatedAt") * (mod(abs(hashtext(t."Id"::text)::bigint), 1000)::double precision / 1000.0)
            FROM production.serial_numbers s
            WHERE t."SerialNumberId" = s."Id" AND s."ProductCode" LIKE 'DEMO-%'
            """,

            // 报工时刻 = 所属工单创建时刻 + 一段比例
            """
            UPDATE production.production_reports r
            SET "ReportedAt" = w."CreatedAt" + (now() - w."CreatedAt") * (mod(abs(hashtext(r."Id"::text)::bigint), 700)::double precision / 1000.0)
            FROM production.work_orders w
            WHERE r."WorkOrderId" = w."Id" AND w."ProductCode" LIKE 'DEMO-%'
            """,

            // ---------- 质量:检验单创建时间决定「一次合格率」落在哪个班次窗口 ----------
            $"""
             UPDATE quality.inspections SET
                 "CreatedAt"   = {Spread("\"InspectionNumber\"")},
                 "InspectedAt" = {Spread("\"InspectionNumber\"")} + interval '35 minutes'
             WHERE "InspectionNumber" LIKE 'DEMO-%'
             """,
            $"""
             UPDATE quality.nonconformances SET
                 "CreatedAt" = {Spread("\"NonconformanceNumber\"")},
                 "DecidedAt" = {Spread("\"NonconformanceNumber\"")} + interval '20 minutes',
                 "ClosedAt"  = CASE WHEN "Status" = 3 THEN {Spread("\"NonconformanceNumber\"")} + interval '3 hours' ELSE NULL END
             WHERE "NonconformanceNumber" LIKE 'DEMO-%'
             """,
            """
            UPDATE quality.repair_records r
            SET "StartedAt"   = n."CreatedAt" + interval '25 minutes',
                "CompletedAt" = n."CreatedAt" + interval '2 hours'
            FROM quality.nonconformances n
            WHERE r."NonconformanceId" = n."Id" AND n."NonconformanceNumber" LIKE 'DEMO-%'
            """,
            $"""
             UPDATE quality.material_lots SET
                 "ReceivedAt"  = {Spread("\"LotNumber\"")},
                 "CreatedAt"   = {Spread("\"LotNumber\"")},
                 "InspectedAt" = {Spread("\"LotNumber\"")} + interval '2 hours'
             WHERE "MaterialCode" LIKE 'DEMO-%'
             """,
            """
            UPDATE quality.sn_material_consumptions c
            SET "BoundAt" = s."CreatedAt" + interval '15 minutes'
            FROM production.serial_numbers s
            WHERE c."Sn" = s."Sn" AND s."ProductCode" LIKE 'DEMO-%'
            """,

            // ---------- 设备 / Andon ----------
            $"""
             UPDATE equipment.andon_calls SET
                 "CalledAt"    = {Spread("\"CallNumber\"")},
                 "EscalatedAt" = CASE WHEN "Escalated" THEN {Spread("\"CallNumber\"")} + interval '15 minutes' ELSE NULL END,
                 "RespondedAt" = CASE WHEN "Status" >= 1 THEN {Spread("\"CallNumber\"")} + interval '6 minutes' ELSE NULL END,
                 "ResolvedAt"  = CASE WHEN "Status" >= 2 THEN {Spread("\"CallNumber\"")} + interval '28 minutes' ELSE NULL END
             WHERE "CallNumber" LIKE 'DEMO-%'
             """,
            "UPDATE equipment.equipments SET \"StatusChangedAt\" = now() - interval '30 minutes' WHERE \"Code\" LIKE 'DEMO-%'",
        ];

        foreach (var statement in statements)
        {
            await ExecuteAsync(statement, cancellationToken);
        }
    }

    // ============================================================
    // 基础设施辅助
    // ============================================================

    private static void Ensure(Result result, string what)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"演示数据生成失败({what}):{result.Error.Code} {result.Error.Description}");
        }
    }

    private DbTransaction? CurrentTransaction => production.Database.CurrentTransaction?.GetDbTransaction();

    private DbCommand CreateCommand(string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 300;
        if (CurrentTransaction is not null)
        {
            command.Transaction = CurrentTransaction;
        }

        return command;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private async Task<int> ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = CreateCommand(sql);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<object?> ExecuteScalarAsync(string sql, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = CreateCommand(sql);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    // ============================================================
    // 内部状态容器
    // ============================================================

    private sealed record Recipe(Product Product, Bom Bom, Routing Routing);

    private sealed record SeededWorkOrder(
        WorkOrder WorkOrder,
        IReadOnlyList<WorkOrderOperation> Operations,
        WorkOrderStatus Status,
        int CompletedOperations);

    private sealed record ProductionCounts
    {
        public int WorkOrders { get; set; }
        public int WorkOrderOperations { get; set; }
        public int SerialNumbers { get; set; }
        public int WipTrackings { get; set; }
        public int ProductionReports { get; set; }
    }

    private sealed record QualityCounts
    {
        public int Inspections { get; set; }
        public int Nonconformances { get; set; }
    }

    private sealed class SeedState
    {
        public List<WorkCenter> Lines { get; } = [];
        public Dictionary<(int LineIndex, string Suffix), WorkCenter> Stations { get; } = [];
        public List<Product> Products { get; } = [];
        public Dictionary<string, Material> MaterialsByName { get; } = [];
        public List<Operation> OperationsInOrder { get; } = [];
        public Dictionary<string, Operation> OperationsByCode { get; } = [];
        public List<Recipe> Recipes { get; } = [];
        public List<EquipmentEntity> Equipments { get; } = [];
        public Dictionary<string, EquipmentEntity> EquipmentByCode { get; } = [];
        public List<SeededWorkOrder> WorkOrders { get; } = [];
        public Dictionary<Guid, List<SerialNumber>> SerialNumbersByWorkOrder { get; } = [];
        public Dictionary<string, MaterialLot> LotsByMaterial { get; } = [];
        public List<MaterialLot> LotsInOrder { get; } = [];

        public int Shifts { get; set; }
        public int CalendarDays { get; set; }
        public int WorkCenters { get; set; }
        public int Boms { get; set; }
        public int Routings { get; set; }
        public int DefectCodes { get; set; }
    }
}
