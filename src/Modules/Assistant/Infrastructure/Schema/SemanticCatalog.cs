namespace QiaoMES.Assistant.Infrastructure.Schema;

/// <summary>某个业务表在语义层里的注解(中文名 / 说明 / 关键词 / 列注解)。</summary>
internal sealed record SemanticTable(
    string Schema,
    string Table,
    string BusinessName,
    string Description,
    string[] Keywords,
    Dictionary<string, SemanticColumn> Columns)
{
    public string Key => $"{Schema}.{Table}";
}

/// <summary>某一列在语义层里的注解。</summary>
internal sealed record SemanticColumn(
    string BusinessName,
    string? Description = null,
    Dictionary<int, string>? EnumValues = null);

/// <summary>
/// 内置语义层目录 —— 这是 Text-to-SQL 效果的上限所在。
/// <para>
/// 列名本身(<c>Status = 3</c>)对模型毫无意义;<b>业务名 + 枚举取值 + 业务口径</b>才有。
/// 真实列结构仍从 <c>information_schema.columns</c> 读取,这里只做"业务注解",两者合并后喂给模型,
/// 因此既不会讲错列名,也能讲清含义。
/// </para>
/// </summary>
internal static class SemanticCatalog
{
    internal static readonly IReadOnlyDictionary<string, SemanticTable> Tables = Build();

    /// <summary>
    /// 业务口径与硬性规则。模型的"常识"在这里必须被纠正:软删除、班次、生产日、指标公式。
    /// </summary>
    internal const string Glossary = """
        ### 使用规则(必须遵守)
        1. 表名一律写全限定名(schema.table)并给别名;所有列名用双引号包裹(PostgreSQL 区分大小写,本库列名是 PascalCase)。
        2. **每张业务表都有 "IsDeleted" 布尔列,必须过滤 `AND 别名."IsDeleted" = false`**,否则会把已软删除的数据算进来。
        3. 不要手工拼 LIMIT,系统会统一给结果集加上界;确需"分组取前 N"时才写 LIMIT。
        4. 百分比返回数值(如 `round(100.0 * a / b, 2)`),不要拼接 '%' 字符。
        5. 结果列请用 `AS "中文名"` 起中文别名,便于直接展示。

        ### 时间与班次口径
        - 班次:白班 `DEMO-DAY` 08:30~20:30;夜班 `DEMO-NIGHT` 20:30~次日 08:30。**跨天夜班归属"开始那一天"作为生产日**。
        - `reporting.shifts` 是班次定义,`reporting.calendar_days` 是生产日历(`IsWorkingDay = false` 表示非生产日,不计入计划生产时间)。
        - `reporting.daily_shift_metrics` 是"生产日 + 班次 + 产线"的预聚合表,**看板/报表类问题优先查它**(快、口径统一);
          明细类问题才回到 `production.serial_numbers` / `quality.inspections` 等明细表做区间聚合。
        - 明细表的时间列:`production.serial_numbers."CreatedAt"`(投产时刻)、`quality.inspections."CreatedAt"`(检验时刻)、
          `production.production_reports."ReportedAt"`(报工时刻)、`equipment.andon_calls."CalledAt"`(呼叫时刻)。

        ### 指标口径
        - 良率 = 完工 SN / (完工 SN + 报废 SN) × 100。对应 `CompletedSn / (CompletedSn + ScrappedSn)`。
        - 一次合格率 FPY = 合格检验单 / (合格 + 不合格 + 让步接收) × 100。
        - 达成率 = 工单完工数 / 计划数 × 100,取 `production.work_orders` 的 `"CompletedQuantity" / "PlannedQuantity"`。
        - OEE = 可用率 × 性能 × 良率,其中可用率 =(计划秒 − 停机秒)/ 计划秒,性能 = 理论工时 / 实际工时。
          `reporting.daily_shift_metrics` 里已经存好了 `PlannedHours / TheoreticalSeconds / ActualSeconds / DowntimeSeconds`,优先用它算。
        - 停机时长与次数来自 `equipment.equipment_status_logs`(以 `ToStatus = 2` 的日志为一次停机开始,下一条日志为结束)。

        ### 枚举速查(整数含义)
        - 工单状态 `work_orders."Status"`:0 草稿 / 1 已下达 / 2 生产中 / 3 已完成 / 4 已取消。
        - 工序任务状态 `work_order_operations."Status"`:0 待开工 / 1 进行中 / 2 已完成。
        - 报工类型 `production_reports."ReportType"`:0 正常报工 / 1 返工。
        - SN 状态 `serial_numbers."Status"`:0 在制 / 1 已完工 / 2 已报废 / 3 已挂起。
        - 过站动作 `wip_trackings."Action"`:0 进站 / 1 出站;结果 `"Result"`:0 无 / 1 合格 / 2 不合格。
        - 检验类型 `inspections."Type"`:0 IQC 来料 / 1 IPQC 过程 / 2 FQC 成品 / 3 OQC 出货;
          状态 `"Status"`:0 待检验 / 1 检验中 / 2 合格 / 3 不合格 / 4 让步接收;
          结论 `"Conclusion"`:0 无 / 1 合格 / 2 不合格 / 3 让步接收。
        - 处置方式 `nonconformances."Disposition"`:0 返工 / 1 返修 / 2 让步接收 / 3 报废 / 4 退货;
          处置状态 `"Status"`:0 待处理 / 1 处理中 / 2 待复检 / 3 已关闭。
        - 物料类型 `materials."MaterialType"`:0 原材料 / 1 半成品 / 2 成品 / 3 辅料耗材。
        - 工作中心类型 `work_centers."Type"`:0 产线 / 1 单元 / 2 工位 / 3 单台设备。
        - 设备状态 `equipments."Status"`:0 运行 / 1 待机 / 2 故障停机 / 3 保养 / 4 离线;
          维护类型 `equipment_maintenance_records."Type"`:0 点检 / 1 保养 / 2 维修。
        - Andon 类型 `andon_calls."Type"`:0 设备故障 / 1 质量异常 / 2 缺料 / 3 其他;等级 `"Level"`:0 黄灯 / 1 红灯;
          状态 `"Status"`:0 待响应 / 1 已响应 / 2 已解决 / 3 已关闭。
        - 来料批次状态 `material_lots."Status"`:0 待检 / 1 合格可用 / 2 不合格 / 3 冻结 / 4 已耗尽。
        """;

    private static Dictionary<string, SemanticTable> Build()
    {
        var tables = new List<SemanticTable>
        {
            new("production", "work_orders", "工单",
                "生产工单主表:计划数、完工数、状态、计划起止、所挂产线,以及下达时快照的 BOM / 工艺路线版本。",
                ["工单", "订单", "计划", "达成率", "在制"],
                new()
                {
                    ["OrderNumber"] = new("工单号"),
                    ["ProductCode"] = new("产品编码"),
                    ["ProductName"] = new("产品名称"),
                    ["PlannedQuantity"] = new("计划数量"),
                    ["CompletedQuantity"] = new("完工数量"),
                    ["Status"] = new("状态", null, new()
                    {
                        [0] = "草稿", [1] = "已下达", [2] = "生产中", [3] = "已完成", [4] = "已取消",
                    }),
                    ["PlannedStart"] = new("计划开工时间"),
                    ["PlannedEnd"] = new("计划完工时间"),
                    ["WorkCenter"] = new("产线名称"),
                    ["RoutingVersion"] = new("工艺路线版本"),
                    ["BomVersion"] = new("BOM 版本"),
                    ["CreatedAt"] = new("创建时间(下达前)"),
                    ["CompletedAt"] = new("完工时间"),
                    ["Remark"] = new("备注"),
                }),

            new("production", "work_order_operations", "工单工序任务",
                "工单下达时按工艺路线展开的工序任务:每道工序的计划数 / 良品 / 不良 / 报废 / 标准工时 / 实际工时。",
                ["工序", "报工", "工时", "产能"],
                new()
                {
                    ["WorkOrderId"] = new("工单 ID"),
                    ["Sequence"] = new("工序顺序(10/20/30…)"),
                    ["OperationCode"] = new("工序编码"),
                    ["OperationName"] = new("工序名称"),
                    ["StandardSeconds"] = new("单件标准工时(秒)"),
                    ["ActualSeconds"] = new("累计实际工时(秒)"),
                    ["PlannedQuantity"] = new("计划数量"),
                    ["GoodQuantity"] = new("良品数"),
                    ["DefectQuantity"] = new("不良数"),
                    ["ScrapQuantity"] = new("报废数"),
                    ["Status"] = new("状态", null, new() { [0] = "待开工", [1] = "进行中", [2] = "已完成" }),
                    ["IsQualityGate"] = new("是否质检点"),
                    ["StartedAt"] = new("开工时间"),
                    ["CompletedAt"] = new("完工时间"),
                }),

            new("production", "production_reports", "报工记录",
                "工序级报工流水:良品 / 不良 / 报废、不良代码、实际工时、执行设备与人员。",
                ["报工", "不良", "工时", "操作员"],
                new()
                {
                    ["WorkOrderId"] = new("工单 ID"),
                    ["WorkOrderOperationId"] = new("工序任务 ID"),
                    ["GoodQuantity"] = new("良品数"),
                    ["DefectQuantity"] = new("不良数"),
                    ["ScrapQuantity"] = new("报废数"),
                    ["DefectCode"] = new("不良代码"),
                    ["WorkedSeconds"] = new("实际工时(秒)"),
                    ["ReportType"] = new("报工类型", null, new() { [0] = "正常报工", [1] = "返工" }),
                    ["OperatorId"] = new("操作员用户 ID"),
                    ["EquipmentId"] = new("执行设备 ID"),
                    ["ReportedAt"] = new("报工时间"),
                    ["Remark"] = new("备注"),
                }),

            new("production", "serial_numbers", "SN 在制品",
                "序列号(单颗产品):当前工序、状态、投产时间与完工时间。产量 / 良率 / 报废率的明细口径。",
                ["SN", "序列号", "在制", "产量", "良率", "报废"],
                new()
                {
                    ["Sn"] = new("序列号"),
                    ["WorkOrderId"] = new("工单 ID"),
                    ["ProductCode"] = new("产品编码"),
                    ["CurrentOperationName"] = new("当前所在工序"),
                    ["LastCompletedOperationName"] = new("最后完成工序"),
                    ["Status"] = new("状态", null, new()
                    {
                        [0] = "在制", [1] = "已完工", [2] = "已报废", [3] = "已挂起",
                    }),
                    ["CreatedAt"] = new("投产时间"),
                    ["CompletedAt"] = new("完工时间"),
                    ["Remark"] = new("备注"),
                }),

            new("production", "wip_trackings", "过站记录",
                "WIP 流转轨迹:每一次进站 / 出站都留痕,含结果、操作人、设备与时刻。追溯与工序停留分析的基础。",
                ["过站", "进站", "出站", "轨迹", "追溯"],
                new()
                {
                    ["SerialNumberId"] = new("SN ID"),
                    ["WorkOrderId"] = new("工单 ID"),
                    ["OperationName"] = new("工序名称"),
                    ["Action"] = new("动作", null, new() { [0] = "进站", [1] = "出站" }),
                    ["Result"] = new("结果", null, new() { [0] = "无", [1] = "合格", [2] = "不合格" }),
                    ["EquipmentId"] = new("设备 ID"),
                    ["OperatorId"] = new("操作员用户 ID"),
                    ["TrackedAt"] = new("过站时间"),
                }),

            new("masterdata", "products", "产品",
                "产品主数据(编码 / 名称 / 规格 / 单位 / 是否启用)。",
                ["产品", "料号", "机型"],
                new()
                {
                    ["Code"] = new("产品编码"),
                    ["Name"] = new("产品名称"),
                    ["Spec"] = new("规格型号"),
                    ["Unit"] = new("单位"),
                    ["IsActive"] = new("是否启用"),
                    ["CreatedAt"] = new("创建时间"),
                }),

            new("masterdata", "materials", "物料",
                "物料主数据(编码 / 名称 / 类型 / 规格 / 单位 / 供应商料号)。",
                ["物料", "来料", "料号", "BOM"],
                new()
                {
                    ["Code"] = new("物料编码"),
                    ["Name"] = new("物料名称"),
                    ["MaterialType"] = new("物料类型", null, new()
                    {
                        [0] = "原材料", [1] = "半成品", [2] = "成品", [3] = "辅料耗材",
                    }),
                    ["Spec"] = new("规格"),
                    ["Unit"] = new("单位"),
                    ["SupplierPartNumber"] = new("供应商料号"),
                    ["IsActive"] = new("是否启用"),
                }),

            new("masterdata", "work_centers", "工作中心",
                "工作中心:产线 / 单元 / 工位 / 单台设备,含所属车间与上级节点。",
                ["产线", "线体", "工位", "车间"],
                new()
                {
                    ["Code"] = new("工作中心编码"),
                    ["Name"] = new("工作中心名称"),
                    ["Type"] = new("类型", null, new()
                    {
                        [0] = "产线", [1] = "单元", [2] = "工位", [3] = "单台设备",
                    }),
                    ["Workshop"] = new("所属车间"),
                    ["ParentId"] = new("上级工作中心 ID"),
                    ["IsActive"] = new("是否启用"),
                }),

            new("quality", "inspections", "检验单",
                "IQC / IPQC / FQC / OQC 检验单:抽样数、允收 / 拒收数、判定结论与不良数。合格率口径。",
                ["检验", "IQC", "IPQC", "FQC", "OQC", "合格率", "抽检"],
                new()
                {
                    ["InspectionNumber"] = new("检验单号"),
                    ["Type"] = new("类型", null, new()
                    {
                        [0] = "IQC 来料", [1] = "IPQC 过程", [2] = "FQC 成品", [3] = "OQC 出货",
                    }),
                    ["Status"] = new("状态", null, new()
                    {
                        [0] = "待检验", [1] = "检验中", [2] = "合格", [3] = "不合格", [4] = "让步接收",
                    }),
                    ["Conclusion"] = new("结论", null, new()
                    {
                        [0] = "无", [1] = "合格", [2] = "不合格", [3] = "让步接收",
                    }),
                    ["Sn"] = new("受检 SN"),
                    ["WorkOrderId"] = new("工单 ID"),
                    ["ProductCode"] = new("产品编码"),
                    ["MaterialCode"] = new("受检物料编码"),
                    ["LotNumber"] = new("来料批次号"),
                    ["SampleSize"] = new("抽样数"),
                    ["AcceptedLimit"] = new("允收数 Ac"),
                    ["RejectedLimit"] = new("拒收数 Re"),
                    ["DefectQuantity"] = new("不良数"),
                    ["InspectorName"] = new("检验员"),
                    ["CreatedAt"] = new("创建时间"),
                    ["InspectedAt"] = new("判定时间"),
                }),

            new("quality", "inspection_items", "检验项",
                "检验单的明细行:项目名、规格上下限、实测值、是否合格与不良代码。SPC 取数来源。",
                ["检验项", "SPC", "实测值", "规格"],
                new()
                {
                    ["InspectionId"] = new("检验单 ID"),
                    ["Name"] = new("检验项名称"),
                    ["Standard"] = new("规格要求"),
                    ["LowerLimit"] = new("规格下限"),
                    ["UpperLimit"] = new("规格上限"),
                    ["IsKeyItem"] = new("是否关键项"),
                    ["MeasuredValue"] = new("实测值(文本)"),
                    ["NumericValue"] = new("实测值(数值)"),
                    ["IsQualified"] = new("是否合格"),
                    ["DefectCode"] = new("不良代码"),
                }),

            new("quality", "nonconformances", "不合格处置单",
                "不合格品处置(NCR):返工 / 返修 / 让步接收 / 报废 / 退货,以及维修与复检闭环状态。",
                ["不合格", "不良", "处置", "返工", "返修", "报废", "NCR"],
                new()
                {
                    ["NonconformanceNumber"] = new("处置单号"),
                    ["Sn"] = new("SN"),
                    ["ProductCode"] = new("产品编码"),
                    ["DefectCode"] = new("不良代码"),
                    ["DefectDescription"] = new("不良描述"),
                    ["Quantity"] = new("数量"),
                    ["Disposition"] = new("处置方式", null, new()
                    {
                        [0] = "返工", [1] = "返修", [2] = "让步接收", [3] = "报废", [4] = "退货",
                    }),
                    ["Status"] = new("状态", null, new()
                    {
                        [0] = "待处理", [1] = "处理中", [2] = "待复检", [3] = "已关闭",
                    }),
                    ["NeedReinspect"] = new("是否需要复检"),
                    ["CreatedAt"] = new("创建时间"),
                    ["DecidedAt"] = new("决策时间"),
                    ["ClosedAt"] = new("关闭时间"),
                }),

            new("quality", "defect_codes", "不良代码",
                "不良代码库:代码 / 名称 / 分类。不良 Pareto 分析的维度表。",
                ["不良代码", "缺陷", "Pareto"],
                new()
                {
                    ["Code"] = new("不良代码"),
                    ["Name"] = new("不良名称"),
                    ["Category"] = new("分类"),
                    ["Description"] = new("说明"),
                    ["IsActive"] = new("是否启用"),
                }),

            new("quality", "material_lots", "来料批次",
                "来料批次台账:供应商、来料数量、剩余数量、IQC 判定结果。上游谱系追溯的起点。",
                ["批次", "来料", "供应商", "IQC", "谱系"],
                new()
                {
                    ["LotNumber"] = new("批次号"),
                    ["MaterialCode"] = new("物料编码"),
                    ["MaterialName"] = new("物料名称"),
                    ["Supplier"] = new("供应商"),
                    ["Quantity"] = new("来料数量"),
                    ["RemainingQuantity"] = new("剩余数量"),
                    ["Unit"] = new("单位"),
                    ["Status"] = new("状态", null, new()
                    {
                        [0] = "待检", [1] = "合格可用", [2] = "不合格", [3] = "冻结", [4] = "已耗尽",
                    }),
                    ["StatusReason"] = new("状态原因"),
                    ["ReceivedAt"] = new("收货时间"),
                    ["InspectedAt"] = new("IQC 判定时间"),
                }),

            new("equipment", "equipments", "设备台账",
                "设备台账与当前状态:编号、型号、所属产线 / 工作中心、状态、累计停机时长。",
                ["设备", "机台", "状态", "停机"],
                new()
                {
                    ["Code"] = new("设备编号"),
                    ["Name"] = new("设备名称"),
                    ["Model"] = new("型号"),
                    ["SerialNumber"] = new("设备序列号"),
                    ["LineName"] = new("所属产线"),
                    ["Status"] = new("状态", null, new()
                    {
                        [0] = "运行", [1] = "待机", [2] = "故障停机", [3] = "保养", [4] = "离线",
                    }),
                    ["StatusReason"] = new("状态说明"),
                    ["DownReasonCode"] = new("停机原因码"),
                    ["TotalDownSeconds"] = new("累计停机时长(秒)"),
                    ["StatusChangedAt"] = new("状态变更时间"),
                    ["IsActive"] = new("是否启用"),
                }),

            new("equipment", "equipment_status_logs", "设备状态轨迹",
                "设备状态变更日志。停机时长 = 以 ToStatus = 2(故障停机)为起点、到下一条日志为止的时间差。",
                ["停机", "状态轨迹", "故障", "原因码"],
                new()
                {
                    ["EquipmentId"] = new("设备 ID"),
                    ["FromStatus"] = new("原状态"),
                    ["ToStatus"] = new("新状态"),
                    ["ReasonCode"] = new("原因码"),
                    ["Reason"] = new("原因说明"),
                    ["ChangedAt"] = new("变更时间"),
                }),

            new("equipment", "andon_calls", "Andon 呼叫",
                "Andon 呼叫单:设备故障 / 质量异常 / 缺料,含等级、超时升级、响应与解决时刻。",
                ["Andon", "呼叫", "告警", "安灯", "响应"],
                new()
                {
                    ["CallNumber"] = new("呼叫单号"),
                    ["Type"] = new("类型", null, new()
                    {
                        [0] = "设备故障", [1] = "质量异常", [2] = "缺料", [3] = "其他",
                    }),
                    ["Level"] = new("等级", null, new() { [0] = "黄灯", [1] = "红灯" }),
                    ["Status"] = new("状态", null, new()
                    {
                        [0] = "待响应", [1] = "已响应", [2] = "已解决", [3] = "已关闭",
                    }),
                    ["EquipmentCode"] = new("设备编号"),
                    ["WorkCenterName"] = new("产线名称"),
                    ["Sn"] = new("关联 SN"),
                    ["Description"] = new("问题描述"),
                    ["TimeoutMinutes"] = new("超时阈值(分钟)"),
                    ["Escalated"] = new("是否已升级红灯"),
                    ["CalledAt"] = new("呼叫时间"),
                    ["EscalatedAt"] = new("升级时间"),
                    ["RespondedAt"] = new("响应时间"),
                    ["ResolvedAt"] = new("解决时间"),
                    ["Resolution"] = new("处理说明"),
                }),

            new("reporting", "shifts", "班次定义",
                "班次定义:白班 / 夜班起止时间、所属产线(空 = 全局)、排序与启停。",
                ["班次", "白班", "夜班", "排班"],
                new()
                {
                    ["Code"] = new("班次编码"),
                    ["Name"] = new("班次名称"),
                    ["StartTime"] = new("开始时间"),
                    ["EndTime"] = new("结束时间"),
                    ["LineName"] = new("所属产线(空 = 全局)"),
                    ["IsActive"] = new("是否启用"),
                }),

            new("reporting", "calendar_days", "生产日历",
                "生产日历:IsWorkingDay = false 表示节假日 / 休息日,不计入计划生产时间。",
                ["日历", "节假日", "调休", "工作日"],
                new()
                {
                    ["Date"] = new("日期"),
                    ["IsWorkingDay"] = new("是否生产日"),
                    ["Name"] = new("名称"),
                    ["Remark"] = new("备注"),
                }),

            new("reporting", "daily_shift_metrics", "班次预聚合指标",
                "按「生产日 + 班次 + 产线」预聚合的指标表。产量 / 质量 / 工时 / 停机四类分量都已算好,"
                + "看板与报表类问题优先查它,口径统一且快。",
                ["OEE", "日报", "班次报表", "汇总", "指标"],
                new()
                {
                    ["ProductionDate"] = new("生产日"),
                    ["ShiftCode"] = new("班次编码"),
                    ["ShiftName"] = new("班次名称"),
                    ["LineName"] = new("产线名称(空 = 全局)"),
                    ["PlannedHours"] = new("计划工时(小时)"),
                    ["TotalSn"] = new("投产 SN 数"),
                    ["CompletedSn"] = new("完工 SN 数"),
                    ["ScrappedSn"] = new("报废 SN 数"),
                    ["InProcessSn"] = new("在制 SN 数"),
                    ["OnHoldSn"] = new("挂起 SN 数"),
                    ["InspectionTotal"] = new("检验单总数"),
                    ["InspectionPassed"] = new("检验合格数"),
                    ["InspectionFailed"] = new("检验不合格数"),
                    ["InspectionConcessioned"] = new("让步接收数"),
                    ["DefectQuantity"] = new("不良数"),
                    ["TheoreticalSeconds"] = new("理论工时(秒) = Σ 标准工时 × 良品数"),
                    ["ActualSeconds"] = new("实际工时(秒)"),
                    ["DowntimeSeconds"] = new("停机时长(秒)"),
                    ["DowntimeCount"] = new("停机次数"),
                }),
        };

        return tables.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
    }
}
