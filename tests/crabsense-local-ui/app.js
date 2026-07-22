/* CrabSense local table UI — plain CRUD against localhost API */
(() => {
  const $ = (id) => document.getElementById(id);
  const msg = (t, isErr) => {
    const el = $("msg");
    el.textContent = typeof t === "string" ? t : JSON.stringify(t, null, 2);
    el.style.borderColor = isErr ? "#c00" : "#999";
  };

  /** Convert datetime-local / date string → ISO UTC for API query/body */
  function toIso(v) {
    if (v == null || v === "") return undefined;
    if (v instanceof Date) return v.toISOString();
    const d = new Date(v);
    if (Number.isNaN(d.getTime())) return String(v);
    return d.toISOString();
  }

  let token = localStorage.getItem("cs_token") || "";
  let user = null;

  const MODULES = [
    {
      id: "areas",
      title: "1. Khu (areas)",
      listPath: "/api/farming-areas",
      filters: [
        { key: "search", label: "search" },
        { key: "isActive", label: "isActive", type: "bool" },
      ],
      create: {
        path: "/api/farming-areas",
        fields: [
          { key: "name", label: "name*", required: true },
          { key: "description", label: "description" },
        ],
      },
      update: {
        path: (id) => `/api/farming-areas/${id}`,
        fields: [
          { key: "name", label: "name*" },
          { key: "description", label: "description" },
          { key: "isActive", label: "isActive", type: "bool", def: true },
        ],
      },
      remove: (id) => `/api/farming-areas/${id}`,
    },
    {
      id: "rows",
      title: "2. Dãy (rows)",
      listPath: "/api/farming-rows",
      filters: [
        { key: "farmingAreaId", label: "farmingAreaId" },
        { key: "search", label: "search" },
        { key: "isActive", label: "isActive", type: "bool" },
      ],
      create: {
        path: "/api/farming-rows",
        fields: [
          { key: "farmingAreaId", label: "farmingAreaId*", required: true },
          { key: "name", label: "name*", required: true },
          { key: "capacity", label: "số hộp (capacity=tạo sẵn)", type: "number", def: 0 },
        ],
      },
      update: {
        path: (id) => `/api/farming-rows/${id}`,
        fields: [
          { key: "name", label: "name*" },
          { key: "capacity", label: "capacity", type: "number" },
          { key: "isActive", label: "isActive", type: "bool", def: true },
        ],
      },
      remove: (id) => `/api/farming-rows/${id}`,
    },
    {
      id: "boxes",
      title: "3. Hộp (boxes)",
      listPath: "/api/boxes",
      filters: [
        { key: "farmingAreaId", label: "farmingAreaId" },
        { key: "farmingRowId", label: "farmingRowId" },
        { key: "code", label: "code" },
        { key: "status", label: "status" },
        { key: "isOccupied", label: "isOccupied", type: "bool" },
      ],
      create: {
        path: "/api/boxes",
        fields: [
          { key: "farmingRowId", label: "farmingRowId*", required: true },
          { key: "farmingAreaId", label: "farmingAreaId (optional)" },
          { key: "code", label: "code (empty=auto BOX-xxxx)" },
        ],
      },
      update: {
        path: (id) => `/api/boxes/${id}`,
        fields: [
          { key: "code", label: "code*" },
          { key: "status", label: "status" },
          { key: "isOccupied", label: "isOccupied", type: "bool", def: false },
        ],
      },
      remove: (id) => `/api/boxes/${id}`,
      extras: [
        {
          label: "GET available",
          run: async (api, filters) => {
            const q = new URLSearchParams();
            if (filters.farmingAreaId) q.set("farmingAreaId", filters.farmingAreaId);
            if (filters.farmingRowId) q.set("farmingRowId", filters.farmingRowId);
            return api("GET", `/api/boxes/available?${q}`);
          },
        },
      ],
    },
    {
      id: "farming-status",
      title: "3b. Trạng thái nuôi hộp",
      listPath: "/api/boxes/farming-status",
      idKey: "boxId",
      columns: [
        "code",
        "status",
        "isOccupied",
        "areaName",
        "rowName",
        "currentCrabTag",
        "currentMoltingStage",
        "currentWeightGram",
        "allocationCount",
        "moltingCount",
        "lastMoltAt",
        "lastMoltResult",
        "boxId",
        "currentCrabId",
      ],
      filters: [
        { key: "farmingAreaId", label: "farmingAreaId" },
        { key: "farmingRowId", label: "farmingRowId" },
        {
          key: "status",
          label: "status",
          type: "select",
          options: ["", "empty", "active", "molting", "quarantine", "maintenance", "harvested"],
          def: "",
        },
      ],
      update: {
        method: "PATCH",
        path: (id) => `/api/boxes/${id}/status`,
        fields: [
          {
            key: "status",
            label: "status",
            type: "select",
            options: ["empty", "active", "molting", "quarantine", "maintenance", "harvested"],
          },
          { key: "isOccupied", label: "isOccupied", type: "bool", def: false },
        ],
      },
      rowActions: [
        { label: "Timeline", method: "GET", path: (id) => `/api/boxes/${id}/farming-timeline` },
        { label: "Status hist", method: "GET", path: (id) => `/api/boxes/${id}/status-history` },
        { label: "Moltings", method: "GET", path: (id) => `/api/boxes/${id}/moltings` },
        { label: "Allocations", method: "GET", path: (id) => `/api/boxes/${id}/allocations` },
      ],
    },
    {
      id: "crabs",
      title: "4. Cua (crabs)",
      listPath: "/api/crabs",
      filters: [],
      create: {
        path: "/api/crabs",
        fields: [
          { key: "crabLotId", label: "crabLotId*", required: true },
          { key: "cropBatchId", label: "cropBatchId*", required: true },
          { key: "boxId", label: "boxId (or empty if auto)" },
          { key: "farmingRowId", label: "farmingRowId (autoAssign)" },
          { key: "farmingAreaId", label: "farmingAreaId (autoAssign)" },
          { key: "autoAssignEmptyBox", label: "autoAssignEmptyBox", type: "bool", def: false },
          { key: "tag", label: "tag" },
          { key: "weightGram", label: "weightGram", type: "number" },
          { key: "moltingStage", label: "moltingStage", type: "select", def: "hard",
            options: ["hard", "premolt", "softshell", "papershell"] },
        ],
      },
      update: {
        path: (id) => `/api/crabs/${id}`,
        fields: [
          { key: "moltingStage", label: "moltingStage", type: "select",
            options: ["hard", "premolt", "softshell", "papershell"] },
          { key: "weightGram", label: "weightGram", type: "number" },
          { key: "isAlive", label: "isAlive", type: "bool", def: true },
          { key: "moltedAt", label: "moltedAt (ISO)" },
        ],
      },
      remove: (id) => `/api/crabs/${id}`,
    },
    {
      id: "lots",
      title: "5. Lô cua (crab-lots)",
      listPath: "/api/crab-lots",
      filters: [],
      create: {
        path: "/api/crab-lots",
        fields: [
          { key: "lotCode", label: "lotCode*", required: true },
          { key: "importDate", label: "importDate* (ISO)", required: true, def: () => new Date().toISOString() },
          { key: "supplierName", label: "supplierName" },
          { key: "notes", label: "notes" },
        ],
      },
      update: {
        path: (id) => `/api/crab-lots/${id}`,
        fields: [
          { key: "supplierName", label: "supplierName" },
          { key: "notes", label: "notes" },
        ],
      },
      remove: (id) => `/api/crab-lots/${id}`,
    },
    {
      id: "batches",
      title: "6. Vụ nuôi (crop-batches)",
      listPath: "/api/crop-batches",
      filters: [],
      create: {
        path: "/api/crop-batches",
        fields: [
          { key: "batchCode", label: "batchCode*", required: true },
          { key: "startDate", label: "startDate* (ISO)", required: true, def: () => new Date().toISOString() },
          { key: "notes", label: "notes" },
        ],
      },
      update: {
        path: (id) => `/api/crop-batches/${id}`,
        fields: [
          { key: "endDate", label: "endDate (ISO)" },
          { key: "status", label: "status" },
          { key: "notes", label: "notes" },
        ],
      },
      remove: (id) => `/api/crop-batches/${id}`,
    },
    {
      id: "sensors",
      title: "7. Sensors + ngưỡng",
      listPath: "/api/sensors",
      columns: [
        "sensorCode",
        "sensorType",
        "unit",
        "minThreshold",
        "maxThreshold",
        "deviceId",
        "isActive",
        "lastSeenAt",
        "id",
      ],
      filters: [{ key: "deviceId", label: "deviceId (lọc theo ESP32)" }],
      create: {
        legend: "Tạo cảm biến — min/max = ngưỡng cảnh báo của cảm biến này",
        path: "/api/sensors",
        fields: [
          { key: "sensorCode", label: "sensorCode*", required: true },
          {
            key: "sensorType",
            label: "sensorType*",
            type: "select",
            def: "Temperature",
            options: ["Temperature", "pH", "DO", "Salinity", "TDS", "Flow", "Level", "Other"],
          },
          { key: "unit", label: "unit", def: "°C" },
          { key: "deviceId", label: "deviceId (ESP32 sở hữu)" },
          { key: "waterSystemId", label: "waterSystemId" },
          { key: "minThreshold", label: "minThreshold (ngưỡng dưới)", type: "number" },
          { key: "maxThreshold", label: "maxThreshold (ngưỡng trên)", type: "number" },
        ],
      },
      update: {
        method: "PUT",
        path: (id) => `/api/sensors/${id}`,
        fields: [
          {
            key: "sensorType",
            label: "sensorType",
            type: "select",
            options: ["Temperature", "pH", "DO", "Salinity", "TDS", "Flow", "Level", "Other"],
          },
          { key: "unit", label: "unit" },
          { key: "deviceId", label: "deviceId (ESP32)" },
          { key: "waterSystemId", label: "waterSystemId" },
          { key: "minThreshold", label: "minThreshold (ngưỡng dưới)", type: "number" },
          { key: "maxThreshold", label: "maxThreshold (ngưỡng trên)", type: "number" },
          { key: "isActive", label: "isActive", type: "bool", def: true },
        ],
      },
      remove: (id) => `/api/sensors/${id}`,
    },
    {
      id: "devices",
      title: "8. Devices (ESP32/Camera)",
      listPath: "/api/devices",
      columns: [
        "deviceCode",
        "deviceType",
        "status",
        "firmwareVersion",
        "batteryLevel",
        "rssiDbm",
        "lastSeenAt",
        "sensorCount",
        "id",
      ],
      filters: [
        {
          key: "status",
          label: "status (heartbeat Extra)",
          type: "select",
          listIgnore: true,
          options: ["", "Online", "Offline", "Maintenance", "Error"],
          def: "",
        },
      ],
      create: {
        path: "/api/devices",
        fields: [
          { key: "deviceCode", label: "deviceCode* (MAC/ESP id)", required: true },
          {
            key: "deviceType",
            label: "deviceType*",
            type: "select",
            def: "esp32",
            options: ["esp32", "camera", "gateway", "other"],
          },
          { key: "firmwareVersion", label: "firmwareVersion" },
          { key: "apiKey", label: "apiKey (optional)" },
        ],
      },
      update: {
        method: "PUT",
        path: (id) => `/api/devices/${id}`,
        fields: [
          {
            key: "deviceType",
            label: "deviceType",
            type: "select",
            options: ["esp32", "camera", "gateway", "other"],
          },
          { key: "firmwareVersion", label: "firmwareVersion" },
          {
            key: "status",
            label: "status",
            type: "select",
            options: ["Online", "Offline", "Maintenance", "Error"],
          },
          { key: "batteryLevel", label: "batteryLevel", type: "number" },
          { key: "rssiDbm", label: "rssiDbm", type: "number" },
        ],
      },
      remove: (id) => `/api/devices/${id}`,
      extras: [
        {
          label: "PUT heartbeat status",
          needsId: true,
          run: async (api, filters, id) => {
            if (!id) throw new Error("Pick id thiết bị");
            return api("PUT", `/api/devices/${id}/status`, {
              status: filters.status || "Online",
            });
          },
        },
      ],
    },
    {
      id: "iot-live",
      title: "8b. Giám sát môi trường",
      listPath: "/api/iot/live",
      columns: [
        "deviceCode",
        "sensorCode",
        "sensorType",
        "latestValue",
        "unit",
        "latestMeasuredAt",
        "alarm",
        "deviceStatus",
        "sensorLastSeenAt",
        "minThreshold",
        "maxThreshold",
      ],
      idKey: "sensorId",
      filters: [{ key: "deviceId", label: "deviceId (lọc ESP32)" }],
      extras: [
        {
          label: "Simulate ESP32 POST reading",
          run: async (api, f, _i, raw) => {
            if (raw && raw.trim()) return api("POST", "/api/iot/sensor-data", JSON.parse(raw));
            throw new Error("Dán JSON: {deviceCode,sensorCode,value,unit,measuredAt}");
          },
        },
        {
          label: "GET history (cần Pick sensorId)",
          needsId: true,
          run: async (api, _f, id) => {
            if (!id) throw new Error("Pick sensorId từ bảng");
            return api("GET", `/api/iot/sensor-data/${id}?page=1&pageSize=50`);
          },
        },
        {
          label: "GET latest (Pick sensorId)",
          needsId: true,
          run: async (api, _f, id) => {
            if (!id) throw new Error("Pick sensorId");
            return api("GET", `/api/iot/sensor-data/${id}/latest`);
          },
        },
        {
          label: "Vẽ chart lịch sử (Pick sensorId)",
          needsId: true,
          run: async (api, _f, id, _r, panel) => {
            if (!id) throw new Error("Pick sensorId");
            const data = await api("GET", `/api/iot/sensor-data/${id}?page=1&pageSize=60`);
            drawSensorHistoryChart(panel, id, data);
            return data;
          },
        },
      ],
      create: {
        legend: "JSON giả lập ESP32 POST /api/iot/sensor-data",
        buttonLabel: "Gửi reading (Extra cũng được)",
        fields: [
          {
            key: "_raw",
            label: "JSON reading",
            type: "textarea",
            def: '{\n  "deviceCode": "ESP32-01",\n  "sensorCode": "TEMP-001",\n  "value": 28.5,\n  "unit": "°C",\n  "measuredAt": "' + new Date().toISOString() + '"\n}',
          },
        ],
        rawPreferred: true,
        path: "/api/iot/sensor-data",
      },
    },
    {
      id: "water-systems",
      title: "8c. Water systems",
      listPath: "/api/water-systems",
      create: {
        path: "/api/water-systems",
        fields: [
          { key: "name", label: "name*", required: true },
          { key: "type", label: "type (RAS/…)" },
          { key: "farmingAreaId", label: "farmingAreaId" },
        ],
      },
      update: {
        method: "PUT",
        path: (id) => `/api/water-systems/${id}`,
        fields: [
          { key: "name", label: "name" },
          { key: "type", label: "type" },
          { key: "farmingAreaId", label: "farmingAreaId" },
          { key: "isActive", label: "isActive", type: "bool", def: true },
        ],
      },
      remove: (id) => `/api/water-systems/${id}`,
    },
    {
      id: "alerts",
      title: "9. Alerts",
      listPath: "/api/alerts",
      filters: [],
      rowActions: [
        { label: "Ack", method: "PATCH", path: (id) => `/api/alerts/${id}/acknowledge` },
        { label: "Resolve", method: "PATCH", path: (id) => `/api/alerts/${id}/resolve` },
      ],
      extras: [
        {
          label: "POST check-disconnects",
          run: (api) => api("POST", "/api/alerts/check-disconnects", {}),
        },
      ],
    },
    {
      id: "thresholds",
      title: "10. Ngưỡng mặc định theo loại",
      listPath: "/api/alert-thresholds",
      filters: [],
      create: {
        legend: "Fallback theo sensorType — chỉ dùng khi cảm biến chưa có min/max riêng",
        path: "/api/alert-thresholds",
        fields: [
          {
            key: "sensorType",
            label: "sensorType*",
            type: "select",
            def: "Temperature",
            options: ["Temperature", "pH", "DO", "Salinity", "TDS", "Flow", "Level", "Other"],
          },
          { key: "minValue", label: "minValue*", type: "number", required: true },
          { key: "maxValue", label: "maxValue*", type: "number", required: true },
          {
            key: "severity",
            label: "severity",
            type: "select",
            def: "Warning",
            options: ["Info", "Warning", "Critical"],
          },
        ],
      },
      update: {
        method: "PUT",
        path: (id) => `/api/alert-thresholds/${id}`,
        fields: [
          { key: "minValue", label: "minValue", type: "number" },
          { key: "maxValue", label: "maxValue", type: "number" },
          {
            key: "severity",
            label: "severity",
            type: "select",
            options: ["Info", "Warning", "Critical"],
          },
          { key: "isActive", label: "isActive", type: "bool", def: true },
        ],
      },
      remove: (id) => `/api/alert-thresholds/${id}`,
    },
    {
      id: "media",
      title: "11. Media",
      listPath: "/api/media",
      filters: [],
      remove: (id) => `/api/media/${id}`,
    },
    {
      id: "iot",
      title: "12. IoT sensor-data",
      listPath: null,
      filters: [{ key: "sensorId", label: "sensorId* for GET" }],
      extras: [
        {
          label: "GET sensor-data/{sensorId}",
          run: async (api, filters) => {
            if (!filters.sensorId) throw new Error("cần sensorId");
            return api("GET", `/api/iot/sensor-data/${filters.sensorId}`);
          },
        },
        {
          label: "POST sensor-data (JSON trong Create raw)",
          run: async (api, _f, _id, raw) => {
            if (!raw) throw new Error("dán JSON vào ô create raw");
            return api("POST", "/api/iot/sensor-data", JSON.parse(raw));
          },
        },
      ],
      create: {
        path: "/api/iot/sensor-data",
        fields: [{ key: "_raw", label: "JSON body", type: "textarea" }],
        rawPreferred: true,
      },
    },
    {
      id: "history",
      title: "13. Lịch sử nuôi / lột xác",
      // Load bảng = lịch sử molting của 1 cua (nhiều dòng cùng ngày OK)
      listPath: (f) => {
        if (!f.crabId) throw new Error("Chọn crabId rồi bấm Load lịch sử");
        const q = new URLSearchParams();
        if (f.from) q.set("from", toIso(f.from));
        if (f.to) q.set("to", toIso(f.to));
        const qs = q.toString();
        return `/api/crabs/${f.crabId}/moltings${qs ? `?${qs}` : ""}`;
      },
      idKey: "id",
      columns: [
        "moltTime",
        "weightAfterGram",
        "result",
        "source",
        "notes",
        "boxId",
        "crabId",
        "id",
      ],
      filters: [
        { key: "crabId", label: "crabId*" },
        { key: "boxId", label: "boxId" },
        { key: "farmingAreaId", label: "farmingAreaId" },
        { key: "farmingRowId", label: "farmingRowId" },
        { key: "from", label: "from (ngày/giờ)", type: "datetime" },
        { key: "to", label: "to (ngày/giờ)", type: "datetime" },
        {
          key: "status",
          label: "boxStatus (filter hộp)",
          type: "select",
          options: ["", "empty", "active", "molting", "quarantine", "maintenance", "harvested"],
          def: "",
        },
      ],
      create: {
        legend: "Nhập đo / ghi lột xác (POST — 1 cua nhiều lần/ngày OK)",
        buttonLabel: "Ghi lần đo / lột xác",
        path: (f) => {
          if (!f.crabId) throw new Error("Chọn crabId ở filter trước khi Create");
          return `/api/crabs/${f.crabId}/moltings`;
        },
        fields: [
          { key: "weightAfterGram", label: "weightAfterGram (gram)", type: "number" },
          {
            key: "result",
            label: "result",
            type: "select",
            options: ["success", "failed", "incomplete"],
            def: "success",
          },
          {
            key: "source",
            label: "source",
            type: "select",
            options: ["manual", "ai"],
            def: "manual",
          },
          {
            key: "moltTime",
            label: "moltTime (trống = giờ hiện tại)",
            type: "datetime",
          },
          {
            key: "notes",
            label: "notes — tình trạng cua (khỏe/thương/vỏ…)",
            type: "text",
            def: "",
          },
        ],
      },
      // Default CRUD targets molting rows (Load lịch sử). Extra loads switch via historyEntity.
      update: {
        method: "PUT",
        path: (id) => `/api/moltings/${id}`,
        fields: [
          { key: "weightAfterGram", label: "weightAfterGram", type: "number" },
          {
            key: "result",
            label: "result",
            type: "select",
            options: ["success", "failed", "incomplete"],
          },
          {
            key: "source",
            label: "source",
            type: "select",
            options: ["manual", "ai"],
          },
          { key: "moltTime", label: "moltTime", type: "datetime" },
          { key: "notes", label: "notes" },
        ],
      },
      remove: (id) => `/api/moltings/${id}`,
      historyEntities: {
        moltings: {
          update: {
            method: "PUT",
            path: (id) => `/api/moltings/${id}`,
            fields: [
              { key: "weightAfterGram", label: "weightAfterGram", type: "number" },
              {
                key: "result",
                label: "result",
                type: "select",
                options: ["success", "failed", "incomplete"],
              },
              { key: "source", label: "source", type: "select", options: ["manual", "ai"] },
              { key: "moltTime", label: "moltTime", type: "datetime" },
              { key: "notes", label: "notes" },
            ],
          },
          remove: (id) => `/api/moltings/${id}`,
        },
        allocations: {
          update: {
            method: "PUT",
            path: (id) => `/api/allocations/${id}`,
            fields: [
              { key: "startTime", label: "startTime", type: "datetime" },
              { key: "endTime", label: "endTime", type: "datetime" },
              { key: "notes", label: "notes" },
            ],
          },
          remove: (id) => `/api/allocations/${id}`,
        },
        statusHistory: {
          update: {
            method: "PUT",
            path: (id) => `/api/box-status-histories/${id}`,
            fields: [
              { key: "oldStatus", label: "oldStatus" },
              {
                key: "newStatus",
                label: "newStatus",
                type: "select",
                options: ["empty", "active", "molting", "quarantine", "maintenance", "harvested"],
              },
              { key: "oldIsOccupied", label: "oldIsOccupied", type: "bool" },
              { key: "newIsOccupied", label: "newIsOccupied", type: "bool" },
              { key: "changedAt", label: "changedAt", type: "datetime" },
              { key: "reason", label: "reason" },
            ],
          },
          remove: (id) => `/api/box-status-histories/${id}`,
        },
      },
      extras: [
        {
          label: "GET farming-status (list hộp)",
          run: async (api, f) => {
            const q = new URLSearchParams();
            if (f.farmingAreaId) q.set("farmingAreaId", f.farmingAreaId);
            if (f.farmingRowId) q.set("farmingRowId", f.farmingRowId);
            if (f.status) q.set("status", f.status);
            return api("GET", `/api/boxes/farming-status?${q}`);
          },
        },
        {
          label: "GET box farming-timeline",
          run: async (api, f) => {
            if (!f.boxId) throw new Error("boxId");
            return api("GET", `/api/boxes/${f.boxId}/farming-timeline`);
          },
        },
        {
          label: "GET allocations by crab",
          entity: "allocations",
          run: async (api, f) => {
            if (!f.crabId) throw new Error("crabId");
            return api("GET", `/api/crabs/${f.crabId}/allocations`);
          },
        },
        {
          label: "GET moltings by box",
          entity: "moltings",
          run: async (api, f) => {
            if (!f.boxId) throw new Error("boxId");
            const q = new URLSearchParams();
            if (f.from) q.set("from", toIso(f.from));
            if (f.to) q.set("to", toIso(f.to));
            const qs = q.toString();
            return api("GET", `/api/boxes/${f.boxId}/moltings${qs ? `?${qs}` : ""}`);
          },
        },
        {
          label: "GET box status-history",
          entity: "statusHistory",
          run: async (api, f) => {
            if (!f.boxId) throw new Error("boxId");
            return api("GET", `/api/boxes/${f.boxId}/status-history`);
          },
        },
        {
          label: "PATCH box status",
          run: async (api, f) => {
            if (!f.boxId || !f.status) throw new Error("boxId + boxStatus");
            return api("PATCH", `/api/boxes/${f.boxId}/status`, {
              status: f.status,
              isOccupied: f.status !== "empty",
            });
          },
        },
      ],
    },
    {
      id: "qr",
      title: "14. Box QR",
      listPath: null,
      filters: [
        { key: "boxId", label: "boxId" },
        { key: "code", label: "qr code", ref: "qrcodes" },
        { key: "crabId", label: "crabId" },
      ],
      extras: [
        {
          label: "POST create QR for box",
          run: async (api, f, _i, _r, panel) => {
            if (!f.boxId) throw new Error("boxId");
            const data = await api("POST", `/api/boxes/${f.boxId}/qr`, {});
            await showBoxQrPng(panel, f.boxId, data?.data?.code || data?.code);
            return data;
          },
        },
        {
          label: "GET QR by box",
          run: async (api, f, _i, _r, panel) => {
            if (!f.boxId) throw new Error("boxId");
            const data = await api("GET", `/api/boxes/${f.boxId}/qr`);
            await showBoxQrPng(panel, f.boxId, data?.data?.code || data?.code);
            return data;
          },
        },
        {
          label: "Hiện ảnh QR PNG (vuông)",
          run: async (api, f, _i, _r, panel) => {
            if (!f.boxId) throw new Error("boxId");
            const meta = await api("GET", `/api/boxes/${f.boxId}/qr`);
            await showBoxQrPng(panel, f.boxId, meta?.data?.code || meta?.code);
            return meta;
          },
        },
        {
          label: "GET scan/by code",
          run: async (api, f) => {
            if (!f.code) throw new Error("code");
            return api("GET", `/api/box-qr/scan?code=${encodeURIComponent(f.code)}`);
          },
        },
        {
          label: "POST move-crab (JSON raw)",
          run: async (api, f, _i, raw) => {
            if (!f.code || !raw) throw new Error("code + JSON");
            return api("POST", `/api/box-qr/${encodeURIComponent(f.code)}/move-crab`, JSON.parse(raw));
          },
        },
      ],
      create: {
        legend: "JSON cho move-crab (Extra)",
        buttonLabel: "Create (không dùng — dùng Extra)",
        fields: [{ key: "_raw", label: "JSON (move-crab)", type: "textarea" }],
        rawPreferred: true,
      },
    },
    {
      id: "harvest",
      title: "15. Harvest vouchers",
      listPath: "/api/harvest-vouchers",
      columns: [
        "voucherCode",
        "harvestDate",
        "status",
        "totalQuantity",
        "totalWeightKg",
        "softshellQuantity",
        "softshellRate",
        "cropBatchId",
        "notes",
        "id",
      ],
      filters: [
        {
          key: "status",
          label: "status (PATCH Extra)",
          type: "select",
          listIgnore: true,
          options: ["", "Planned", "InProgress", "Completed", "Cancelled"],
          def: "InProgress",
        },
        { key: "from", label: "from (statistics)", type: "datetime", listIgnore: true },
        { key: "to", label: "to (statistics)", type: "datetime", listIgnore: true },
        {
          key: "period",
          label: "period",
          type: "select",
          listIgnore: true,
          options: ["day", "week", "month"],
          def: "day",
        },
      ],
      create: {
        legend: "Tạo phiếu thu hoạch — dùng JSON raw (Lines[]) hoặc form + linesJson",
        path: "/api/harvest-vouchers",
        rawPreferred: true,
        fields: [
          {
            key: "_raw",
            label: "JSON body CreateHarvestVoucherRequest",
            type: "textarea",
            def:
              '{\n  "cropBatchId": null,\n  "harvestDate": "' +
              new Date().toISOString() +
              '",\n  "notes": "demo harvest",\n  "lines": [\n    { "crabId": null, "weightGram": 250, "grade": "M", "isSoftshell": true, "notes": null },\n    { "crabId": null, "weightGram": 280, "grade": "L", "isSoftshell": false, "notes": null }\n  ]\n}',
          },
          { key: "cropBatchId", label: "cropBatchId (nếu không dùng raw)" },
          { key: "harvestDate", label: "harvestDate", type: "datetime", def: () => new Date().toISOString() },
          { key: "notes", label: "notes" },
          {
            key: "lines",
            label: "lines (JSON array — nếu không dùng raw)",
            type: "textarea",
            def: '[{"crabId":null,"weightGram":250,"grade":"M","isSoftshell":true}]',
          },
        ],
      },
      update: {
        method: "PATCH",
        legend: "Chỉ đổi status (Planned→InProgress→Completed | Cancelled)",
        path: (id) => `/api/harvest-vouchers/${id}/status`,
        fields: [
          {
            key: "status",
            label: "status*",
            type: "select",
            options: ["Planned", "InProgress", "Completed", "Cancelled"],
            def: "InProgress",
          },
        ],
      },
      remove: (id) => `/api/harvest-vouchers/${id}`,
      rowActions: [
        {
          label: "→InProgress",
          method: "PATCH",
          path: (id) => `/api/harvest-vouchers/${id}/status`,
          body: () => ({ status: "InProgress" }),
        },
        {
          label: "→Completed",
          method: "PATCH",
          path: (id) => `/api/harvest-vouchers/${id}/status`,
          body: () => ({ status: "Completed" }),
        },
        {
          label: "→Cancelled",
          method: "PATCH",
          path: (id) => `/api/harvest-vouchers/${id}/status`,
          body: () => ({ status: "Cancelled" }),
        },
      ],
      extras: [
        {
          label: "GET by id (Pick)",
          needsId: true,
          run: async (api, _f, id) => {
            if (!id) throw new Error("Pick voucher id");
            return api("GET", `/api/harvest-vouchers/${id}`);
          },
        },
        {
          label: "GET statistics (from/to/period)",
          run: async (api, f) => {
            const from = f.from || new Date(Date.now() - 30 * 864e5).toISOString();
            const to = f.to || new Date().toISOString();
            const period = f.period || "day";
            const q = new URLSearchParams({ from, to, period });
            return api("GET", `/api/harvest-vouchers/statistics?${q}`);
          },
        },
        {
          label: "PATCH status (Pick + filter status)",
          needsId: true,
          run: async (api, f, id) => {
            if (!id) throw new Error("Pick id");
            if (!f.status) throw new Error("Chọn status ở filter");
            return api("PATCH", `/api/harvest-vouchers/${id}/status`, { status: f.status });
          },
        },
      ],
    },
    {
      id: "frozen",
      title: "16. Frozen lots",
      listPath: "/api/frozen-lots",
      columns: [
        "lotCode",
        "frozenDate",
        "expiryDate",
        "weightKg",
        "grade",
        "quantity",
        "status",
        "storageLocation",
        "harvestVoucherId",
        "id",
      ],
      filters: [
        { key: "days", label: "days (expiring)", type: "number", def: 30, listIgnore: true },
      ],
      create: {
        path: "/api/frozen-lots",
        fields: [
          { key: "lotCode", label: "lotCode*", required: true, def: () => `FL-${Date.now()}` },
          { key: "harvestVoucherId", label: "harvestVoucherId" },
          {
            key: "frozenDate",
            label: "frozenDate*",
            type: "datetime",
            required: true,
            def: () => new Date().toISOString(),
          },
          {
            key: "expiryDate",
            label: "expiryDate*",
            type: "datetime",
            required: true,
            def: () => new Date(Date.now() + 90 * 864e5).toISOString(),
          },
          { key: "weightKg", label: "weightKg*", type: "number", required: true, def: 10 },
          {
            key: "grade",
            label: "grade",
            type: "select",
            options: ["", "S", "M", "L", "XL"],
            def: "M",
          },
          { key: "quantity", label: "quantity*", type: "number", required: true, def: 20 },
          { key: "storageLocation", label: "storageLocation", def: "Cold-A1" },
        ],
      },
      update: {
        method: "PUT",
        path: (id) => `/api/frozen-lots/${id}`,
        fields: [
          { key: "frozenDate", label: "frozenDate", type: "datetime" },
          { key: "expiryDate", label: "expiryDate", type: "datetime" },
          { key: "weightKg", label: "weightKg", type: "number" },
          { key: "grade", label: "grade", type: "select", options: ["S", "M", "L", "XL"] },
          { key: "quantity", label: "quantity", type: "number" },
          {
            key: "status",
            label: "status",
            type: "select",
            options: ["Available", "Reserved", "Shipped", "Expired"],
            def: "Available",
          },
          { key: "storageLocation", label: "storageLocation" },
        ],
      },
      remove: (id) => `/api/frozen-lots/${id}`,
      extras: [
        {
          label: "GET inventory-summary",
          run: (api) => api("GET", "/api/frozen-lots/inventory-summary"),
        },
        {
          label: "GET storage-aging",
          run: (api) => api("GET", "/api/frozen-lots/storage-aging"),
        },
        {
          label: "GET expiring (days filter)",
          run: async (api, f) => {
            const days = f.days ?? 30;
            return api("GET", `/api/frozen-lots/expiring?days=${days}`);
          },
        },
        {
          label: "GET by id (Pick)",
          needsId: true,
          run: async (api, _f, id) => {
            if (!id) throw new Error("Pick lot id");
            return api("GET", `/api/frozen-lots/${id}`);
          },
        },
      ],
    },
    {
      id: "reports",
      title: "17. Reports (sysadmin)",
      listPath: null,
      filters: [],
      extras: [
        {
          label: "GET /api/reports/harvest (cần sysadmin)",
          run: (api) => api("GET", "/api/reports/harvest"),
        },
        {
          label: "GET /api/reports/inventory (cần sysadmin)",
          run: (api) => api("GET", "/api/reports/inventory"),
        },
      ],
    },
    {
      id: "skeleton",
      title: "18. Skeleton (GET only)",
      listPath: null,
      filters: [],
      extras: [
        { label: "customers", run: (api) => api("GET", "/api/customers") },
        { label: "price-lists", run: (api) => api("GET", "/api/price-lists") },
        { label: "sales-orders", run: (api) => api("GET", "/api/sales-orders") },
        { label: "deliveries", run: (api) => api("GET", "/api/deliveries") },
        { label: "payments", run: (api) => api("GET", "/api/payments") },
        { label: "dashboard/overview", run: (api) => api("GET", "/api/dashboard/overview") },
        { label: "settings", run: (api) => api("GET", "/api/settings") },
        { label: "ai/detections", run: (api) => api("GET", "/api/ai/detections") },
        { label: "ai/recommendations", run: (api) => api("GET", "/api/ai/recommendations") },
        { label: "GET /api/sync/queue", run: (api) => api("GET", "/api/sync/queue") },
        { label: "GET /api/v1/sync/pull", run: (api) => api("GET", "/api/v1/sync/pull") },
        { label: "GET /api/v1/sync/changes", run: (api) => api("GET", "/api/v1/sync/changes") },
        { label: "GET /api/v1/sync/download", run: (api) => api("GET", "/api/v1/sync/download") },
        { label: "GET /api/v1/synchronization/queue", run: (api) => api("GET", "/api/v1/synchronization/queue") },
      ],
    },
    {
      id: "notifications",
      title: "19. Notifications / kênh",
      listPath: "/api/notifications/channels",
      columns: ["channelCode", "displayName", "isEnabled", "configJson", "id"],
      filters: [{ key: "userId", label: "userId (in-app inbox)", listIgnore: true }],
      create: {
        legend: "Thêm kênh (telegram / zalo_oa / push / email / custom)",
        path: "/api/notifications/channels",
        fields: [
          { key: "channelCode", label: "channelCode* (telegram|zalo_oa|push|email|…)", required: true, def: "telegram" },
          { key: "displayName", label: "displayName*", required: true, def: "Telegram" },
          { key: "isEnabled", label: "isEnabled", type: "bool", def: false },
          {
            key: "configJson",
            label: "configJson (Telegram: bot_token+chat_id | Zalo: access_token+user_id)",
            type: "textarea",
            def: '{\n  "bot_token": "",\n  "chat_id": ""\n}',
          },
        ],
      },
      update: {
        method: "PUT",
        path: (id) => `/api/notifications/channels/${id}`,
        fields: [
          { key: "displayName", label: "displayName" },
          { key: "isEnabled", label: "isEnabled", type: "bool", def: false },
          {
            key: "configJson",
            label: "configJson",
            type: "textarea",
            def: '{\n  "bot_token": "",\n  "chat_id": ""\n}',
          },
        ],
      },
      remove: (id) => `/api/notifications/channels/${id}`,
      rowActions: [
        {
          label: "Test",
          method: "POST",
          path: (id) => `/api/notifications/channels/${id}/test`,
          body: () => ({ title: "CrabSense test", body: "Test từ Local UI" }),
        },
      ],
      extras: [
        {
          label: "GET in-app notifications (userId)",
          run: async (api, f) => {
            const uid = f.userId || user?.id;
            if (!uid) throw new Error("userId");
            return api("GET", `/api/notifications/user/${uid}`);
          },
        },
        {
          label: "Mark read (Pick notification id)",
          needsId: true,
          run: async (api, _f, id) => {
            if (!id) throw new Error("Pick id notification (sau khi Load inbox)");
            return api("PATCH", `/api/notifications/${id}/read`);
          },
        },
        {
          label: "Test channel (Pick + JSON raw title/body)",
          needsId: true,
          run: async (api, _f, id, raw) => {
            if (!id) throw new Error("Pick channel id");
            const body = raw && raw.trim() ? JSON.parse(raw) : { title: "CrabSense test", body: "OK" };
            return api("POST", `/api/notifications/channels/${id}/test`, body);
          },
        },
        {
          label: "POST register FCM token",
          run: async (api) =>
            api("POST", "/api/notifications/register", {
              token: "demo-fcm-token-" + Date.now(),
              platform: "android",
              deviceId: "local-ui-device",
            }),
        },
        {
          label: "GET push tokens (current user)",
          run: async (api) => api("GET", "/api/notifications/register"),
        },
        {
          label: "GET notification settings",
          run: async (api) => api("GET", "/api/notifications/settings"),
        },
        {
          label: "PUT notification settings (Telegram stub)",
          run: async (api) =>
            api("PUT", "/api/notifications/settings", {
              pushEnabled: true,
              telegram: {
                enabled: false,
                configJson: JSON.stringify({ bot_token: "", chat_id: "" }),
              },
              zalo: {
                enabled: false,
                configJson: JSON.stringify({ access_token: "", user_id: "" }),
              },
            }),
        },
        {
          label: "GET v1 notification settings (Mobile alias)",
          run: async (api) => api("GET", "/api/v1/notifications/settings"),
        },
        {
          label: "POST v1 register FCM (Mobile alias)",
          run: async (api) =>
            api("POST", "/api/v1/notifications/register", {
              token: "demo-fcm-v1-" + Date.now(),
              platform: "android",
              deviceId: "local-ui-v1",
            }),
        },
      ],
    },
  ];

  let currentId = "areas";
  const state = {}; // per module filter/form values + rows

  function baseUrl() {
    return ($("apiBase").value || "http://localhost:5080").replace(/\/$/, "");
  }

  function clearSession(message) {
    token = "";
    user = null;
    refs.loaded = false;
    localStorage.removeItem("cs_token");
    $("app").style.display = "none";
    $("loginInfo").textContent = message || "Đăng nhập lại";
  }

  async function api(method, path, body) {
    const headers = { Accept: "application/json" };
    if (token) headers.Authorization = `Bearer ${token}`;
    if (body !== undefined) headers["Content-Type"] = "application/json";
    const res = await fetch(baseUrl() + path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await res.text();
    let data;
    try {
      data = text ? JSON.parse(text) : null;
    } catch {
      data = text;
    }
    if (!res.ok) {
      if (res.status === 401 && path !== "/api/auth/login") {
        clearSession("Token hết hạn — bấm Login lại");
      }
      const err = new Error(`${res.status} ${method} ${path}`);
      err.payload = data;
      throw err;
    }
    return data;
  }

  /** Binary GET (QR PNG) with auth. */
  async function apiBlob(path) {
    const headers = { Accept: "image/png,*/*" };
    if (token) headers.Authorization = `Bearer ${token}`;
    const res = await fetch(baseUrl() + path, { method: "GET", headers });
    if (!res.ok) {
      const text = await res.text();
      const err = new Error(`${res.status} GET ${path}`);
      err.payload = text;
      throw err;
    }
    return res.blob();
  }

  async function showBoxQrPng(panel, boxId, codeHint) {
    if (!panel || !boxId) return;
    let hold = panel.querySelector("[data-qr-preview]");
    if (!hold) {
      hold = document.createElement("fieldset");
      hold.dataset.qrPreview = "1";
      hold.innerHTML = "<legend>Ảnh QR vuông (PNG)</legend><div data-qr-preview-body></div>";
      const extras = [...panel.querySelectorAll("fieldset")].find((f) =>
        (f.querySelector("legend")?.textContent || "").includes("Extra")
      );
      if (extras) panel.insertBefore(hold, extras);
      else panel.appendChild(hold);
    }
    const body = hold.querySelector("[data-qr-preview-body]");
    body.innerHTML = "<i>Đang tải PNG…</i>";
    try {
      const blob = await apiBlob(`/api/boxes/${boxId}/qr.png?size=10`);
      const url = URL.createObjectURL(blob);
      const code = codeHint || "(xem JSON)";
      body.innerHTML = `
        <div style="display:flex;gap:16px;align-items:flex-start;flex-wrap:wrap">
          <img src="${url}" alt="QR" width="220" height="220"
            style="image-rendering:pixelated;border:1px solid #000;background:#fff" />
          <div>
            <div><b>code:</b> <code>${escapeHtml(code)}</code></div>
            <div style="margin-top:8px">
              <a href="${url}" download="box-qr.png">Tải PNG</a>
              &nbsp;|&nbsp;
              <button type="button" data-print-qr>In / mở tab</button>
            </div>
            <p style="opacity:.85;max-width:280px;margin-top:8px">
              API: <code>GET /api/boxes/{boxId}/qr.png</code> — ảnh vuông in tem.<br/>
              Quét: gõ <b>code</b> rồi <b>GET scan/by code</b> (chưa có upload ảnh camera).
            </p>
          </div>
        </div>`;
      body.querySelector("[data-print-qr]")?.addEventListener("click", () => {
        const w = window.open(url);
        if (w) setTimeout(() => w.print(), 400);
      });
      // fill code filter
      const codeInp = panel.querySelector(`[data-f="code"]`);
      const codeDd = panel.querySelector(`[data-dd="code"]`);
      if (codeInp && codeHint) {
        codeInp.value = codeHint;
        if (codeDd) codeDd.value = codeHint;
      }
    } catch (e) {
      body.innerHTML = `<span style="color:#c00">${escapeHtml(e.payload || e.message)}</span>`;
      throw e;
    }
  }

  function unwrapList(payload) {
    const d = payload?.data ?? payload;
    if (Array.isArray(d)) return d;
    if (d && Array.isArray(d.items)) return d.items;
    if (d && typeof d === "object") return [d];
    return [];
  }

  let refs = { areas: [], rows: [], boxes: [], crabs: [], lots: [], batches: [], loaded: false };

  async function ensureRefs(force) {
    if (refs.loaded && !force) return refs;
    const [areas, rows, boxes, crabs, lots, batches] = await Promise.all([
      api("GET", "/api/farming-areas").then(unwrapList).catch(() => []),
      api("GET", "/api/farming-rows").then(unwrapList).catch(() => []),
      api("GET", "/api/boxes").then(unwrapList).catch(() => []),
      api("GET", "/api/crabs").then(unwrapList).catch(() => []),
      api("GET", "/api/crab-lots").then(unwrapList).catch(() => []),
      api("GET", "/api/crop-batches").then(unwrapList).catch(() => []),
    ]);
    refs = { areas, rows, boxes, crabs, lots, batches, loaded: true };
    // enrich crab labels: box code from boxes list
    const boxById = new Map((boxes || []).map((b) => [String(b.id), b]));
    for (const c of refs.crabs || []) {
      const b = boxById.get(String(c.boxId));
      if (b) c.boxCode = b.code;
    }
    return refs;
  }

  function refKind(key, label) {
    const k = (key || "").toLowerCase();
    const lab = (label || "").toLowerCase();
    if (k === "farmingareaid" || k === "areaid") return "areas";
    if (k === "farmingrowid" || k === "rowid") return "rows";
    if (k === "boxid") return "boxes";
    if (k === "crabid") return "crabs";
    if (k === "crablotid") return "lots";
    if (k === "cropbatchid") return "batches";
    if (k === "code" && lab.includes("qr")) return "qrcodes";
    if (k === "userid" || k === "ownerid") return "users";
    if (k.endsWith("id") && k !== "id") return "guid";
    return null;
  }

  function optionLabel(kind, row) {
    if (!row) return "";
    if (kind === "areas") return `${row.name || "?"} | ${row.id}`;
    if (kind === "rows") return `${row.name || "?"} / ${row.areaName || ""} | ${row.id}`;
    if (kind === "boxes") return `${row.code || "?"} ${row.isOccupied ? "[full]" : "[empty]"} | ${row.id}`;
    if (kind === "crabs")
      return `${row.tag || "(no tag)"} stage=${row.moltingStage || "?"} w=${row.weightGram ?? "?"} box=${row.boxCode || row.boxId || "?"} alive=${row.isAlive} | ${row.id}`;
    if (kind === "lots") return `${row.lotCode || "?"} | ${row.id}`;
    if (kind === "batches") return `${row.batchCode || "?"} | ${row.id}`;
    if (kind === "qrcodes") return `${row.code || row.id}`;
    return String(row.id || "");
  }

  function listForKind(kind) {
    if (kind === "qrcodes") {
      // placeholder — filled when QR fetched; use box codes as fallback tips
      return (refs.boxes || []).map((b) => ({ id: b.code, code: b.code, _boxId: b.id }));
    }
    if (kind === "guid") return [];
    return refs[kind] || [];
  }

  function fieldValue(f, container) {
    const el = container.querySelector(`[data-f="${f.key}"]`);
    if (!el) return undefined;
    if (f.type === "bool") {
      if (el.value === "") return undefined;
      return el.value === "true";
    }
    if (f.type === "number") {
      if (el.value === "") return undefined;
      return Number(el.value);
    }
    if (f.type === "datetime") {
      if (el.value === "") return undefined;
      return toIso(el.value);
    }
    if (el.value === "") return undefined;
    return el.value;
  }

  function buildBody(fields, container, rawPreferred) {
    const rawEl = container.querySelector(`[data-f="_raw"]`);
    if (rawPreferred && rawEl && rawEl.value.trim()) {
      return JSON.parse(rawEl.value);
    }
    const body = {};
    for (const f of fields || []) {
      if (f.key === "_raw") continue;
      let v = fieldValue(f, container);
      if (v === undefined) continue;
      // Parse JSON array/object fields (e.g. harvest lines)
      if (typeof v === "string" && (f.key === "lines" || f.parseJson) && /^[\[{]/.test(v.trim())) {
        v = JSON.parse(v);
      }
      body[f.key] = v;
    }
    return body;
  }

  function renderField(f) {
    const wrap = document.createElement("span");
    wrap.style.display = "inline-block";
    wrap.style.marginRight = "10px";
    wrap.style.marginBottom = "4px";
    wrap.dataset.fieldWrap = f.key;

    let def = "";
    if (typeof f.def === "function") def = f.def();
    else if (f.def !== undefined && f.def !== null) def = String(f.def);

    const kind = f.ref || refKind(f.key, f.label);

    if (f.type === "textarea") {
      wrap.innerHTML = `<label>${f.label}<br/><textarea data-f="${f.key}" rows="3" cols="60">${escapeHtml(def)}</textarea></label>`;
    } else if (f.type === "datetime") {
      wrap.innerHTML = `<label>${f.label} <input type="datetime-local" data-f="${f.key}" value="${escapeAttr(def)}" /></label>`;
    } else if (f.type === "bool") {
      wrap.innerHTML = `<label>${f.label} <select data-f="${f.key}">
        <option value="">(any/omit)</option>
        <option value="true" ${def === "true" ? "selected" : ""}>true</option>
        <option value="false" ${def === "false" ? "selected" : ""}>false</option>
      </select></label>`;
    } else if (f.type === "select" && Array.isArray(f.options)) {
      const opts = f.options
        .map((o) => {
          const v = String(o);
          return `<option value="${escapeAttr(v)}" ${def === v ? "selected" : ""}>${escapeHtml(v)}</option>`;
        })
        .join("");
      wrap.innerHTML = `<label>${f.label} <select data-f="${f.key}">${opts}</select></label>`;
    } else if (kind) {
      const listId = `dl-${f.key}-${Math.random().toString(36).slice(2, 7)}`;
      wrap.innerHTML = `<label>${f.label}
        <input data-f="${f.key}" data-ref="${kind}" list="${listId}" value="${escapeAttr(def)}" size="34" placeholder="gõ hoặc chọn ↓" />
        <datalist id="${listId}"></datalist>
        <select data-dd="${f.key}" title="dropdown nhanh">
          <option value="">— chọn —</option>
        </select>
      </label>`;
      wrap.dataset.refKind = kind;
      wrap.dataset.listId = listId;
    } else {
      wrap.innerHTML = `<label>${f.label} <input data-f="${f.key}" value="${escapeAttr(def)}" size="${f.type === "number" ? 8 : 28}" /></label>`;
    }
    return wrap;
  }

  function fillComboOptions(wrap, rows, kind, valueKey) {
    const listId = wrap.dataset.listId;
    const dl = listId ? document.getElementById(listId) : null;
    const sel = wrap.querySelector(`[data-dd]`);
    const vk = valueKey || "id";
    if (dl) {
      dl.innerHTML = rows
        .map((r) => {
          const v = kind === "qrcodes" ? r.code || r.id : r[vk];
          return `<option value="${escapeAttr(v)}">${escapeHtml(optionLabel(kind, r))}</option>`;
        })
        .join("");
    }
    if (sel) {
      const cur = wrap.querySelector(`[data-f]`)?.value || "";
      sel.innerHTML =
        `<option value="">— chọn —</option>` +
        rows
          .map((r) => {
            const v = kind === "qrcodes" ? r.code || r.id : r[vk];
            const selAttr = String(v) === String(cur) ? " selected" : "";
            return `<option value="${escapeAttr(v)}"${selAttr}>${escapeHtml(optionLabel(kind, r))}</option>`;
          })
          .join("");
    }
  }

  function wireCombo(wrap, panel) {
    const inp = wrap.querySelector("[data-f]");
    const sel = wrap.querySelector("[data-dd]");
    if (!inp || !sel) return;
    sel.onchange = () => {
      if (sel.value) {
        inp.value = sel.value;
        inp.dispatchEvent(new Event("change", { bubbles: true }));
      }
    };
    inp.addEventListener("change", () => onIdFieldChanged(panel, inp));
    inp.addEventListener("input", () => {
      // keep select in sync if exact match
      const opt = Array.from(sel.options).find((o) => o.value === inp.value);
      sel.value = opt ? inp.value : "";
    });
  }

  function ensureJsQr() {
    if (typeof jsQR === "function") return;
    throw new Error("jsQR chưa tải — kiểm tra mạng / CDN jsqr trong index.html");
  }

  function loadImageFromFile(file) {
    return new Promise((resolve, reject) => {
      const url = URL.createObjectURL(file);
      const img = new Image();
      img.onload = () => {
        URL.revokeObjectURL(url);
        resolve(img);
      };
      img.onerror = () => {
        URL.revokeObjectURL(url);
        reject(new Error("Không đọc được ảnh"));
      };
      img.src = url;
    });
  }

  async function decodeQrCodeFromFile(file) {
    ensureJsQr();
    const img = await loadImageFromFile(file);
    const canvas = document.createElement("canvas");
    // Upscale small photos a bit for better decode
    const maxSide = 1200;
    let { width: w, height: h } = img;
    const scale = Math.min(1, maxSide / Math.max(w, h));
    w = Math.max(1, Math.round(w * (scale < 1 ? maxSide / Math.max(img.width, img.height) : 1)));
    h = Math.max(1, Math.round(h * (w / img.width)));
    if (Math.max(img.width, img.height) > maxSide) {
      const s = maxSide / Math.max(img.width, img.height);
      w = Math.round(img.width * s);
      h = Math.round(img.height * s);
    } else {
      w = img.width;
      h = img.height;
    }
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    ctx.drawImage(img, 0, 0, w, h);
    let imageData = ctx.getImageData(0, 0, w, h);
    let result = jsQR(imageData.data, imageData.width, imageData.height, {
      inversionAttempts: "attemptBoth",
    });
    if (!result && w < 800) {
      // retry larger
      const s = 2;
      canvas.width = w * s;
      canvas.height = h * s;
      ctx.imageSmoothingEnabled = false;
      ctx.drawImage(img, 0, 0, w * s, h * s);
      imageData = ctx.getImageData(0, 0, w * s, h * s);
      result = jsQR(imageData.data, imageData.width, imageData.height, {
        inversionAttempts: "attemptBoth",
      });
    }
    if (!result?.data) throw new Error("Không thấy QR trong ảnh — thử ảnh rõ / gần hơn");
    return String(result.data).trim();
  }

  function renderScanResult(panel, data, decodedCode) {
    let hold = panel.querySelector("[data-qr-scan-result]");
    if (!hold) {
      hold = document.createElement("fieldset");
      hold.dataset.qrScanResult = "1";
      hold.innerHTML = "<legend>Kết quả quét QR</legend><div data-qr-scan-result-body></div>";
      const scanBox = panel.querySelector("[data-qr-scan-upload]");
      if (scanBox && scanBox.nextSibling) panel.insertBefore(hold, scanBox.nextSibling);
      else panel.appendChild(hold);
    }
    const body = hold.querySelector("[data-qr-scan-result-body]");
    const d = data?.data ?? data;
    const qr = d?.qr || d?.Qr;
    const box = d?.box || d?.Box;
    const crabs = d?.crabs || d?.Crabs || [];
    const code = qr?.code || decodedCode || "";
    const areaName = d?.areaName || d?.AreaName || "";
    const rowName = d?.rowName || d?.RowName || "";
    let html = `<p><b>code:</b> <code>${escapeHtml(code)}</code></p>`;
    if (box) {
      html += `<table>
        <tr><th>hộp</th><td>${escapeHtml(box.code || "")} | ${escapeHtml(box.id || "")}</td></tr>
        <tr><th>status</th><td>${escapeHtml(box.status || "")}</td></tr>
        <tr><th>occupied</th><td>${escapeHtml(box.isOccupied)}</td></tr>
        <tr><th>dãy / khu</th><td>${escapeHtml(rowName)} / ${escapeHtml(areaName)}</td></tr>
      </table>`;
    }
    html += `<p><b>Cua trong hộp (${crabs.length}):</b></p>`;
    if (!crabs.length) html += "<i>(không có cua)</i>";
    else {
      html += "<table><tr><th>tag</th><th>stage</th><th>weight</th><th>alive</th><th>id</th><th></th></tr>";
      for (const c of crabs) {
        html += `<tr>
          <td>${escapeHtml(c.tag || "")}</td>
          <td>${escapeHtml(c.moltingStage || "")}</td>
          <td>${escapeHtml(c.weightGram ?? "")}</td>
          <td>${escapeHtml(c.isAlive)}</td>
          <td class="id">${escapeHtml(c.id || "")}</td>
          <td><button type="button" data-use-crab="${escapeAttr(c.id || "")}">Dùng crabId</button></td>
        </tr>`;
      }
      html += "</table>";
    }
    html += `<details open><summary>JSON đầy đủ</summary><pre style="white-space:pre-wrap;font-size:11px">${escapeHtml(
      JSON.stringify(data, null, 2)
    )}</pre></details>`;
    body.innerHTML = html;
    body.querySelectorAll("[data-use-crab]").forEach((btn) => {
      btn.onclick = () => setComboValue(panel, "crabId", btn.getAttribute("data-use-crab"));
    });
  }

  function mountLiveMonitor(panel) {
    if (panel.querySelector("[data-live-monitor]")) return;
    const box = document.createElement("fieldset");
    box.dataset.liveMonitor = "1";
    box.innerHTML = `
      <legend>Giám sát môi trường — Live + chart</legend>
      <p style="margin:4px 0;opacity:.9">
        Theo dõi Temperature / pH / Salinity / DO / Level. Bấm Refresh hoặc auto 15s.
        Pick sensorId rồi <b>Vẽ chart lịch sử</b> (tab Extra / nút dưới).
      </p>
      <div data-live-cards style="display:flex;flex-wrap:wrap;gap:8px;margin:8px 0"></div>
      <label>Auto refresh
        <input type="checkbox" data-live-auto />
      </label>
      <button type="button" data-live-refresh>Refresh live</button>
      <button type="button" data-live-chart>Vẽ chart sensor đã Pick</button>
      <div style="max-width:900px;margin-top:8px">
        <canvas data-live-chart height="120"></canvas>
      </div>
    `;
    const filter = panel.querySelector("fieldset");
    if (filter && filter.nextSibling) panel.insertBefore(box, filter.nextSibling);
    else panel.appendChild(box);

    const auto = box.querySelector("[data-live-auto]");
    box.querySelector("[data-live-refresh]").onclick = () => loadList(panel);
    box.querySelector("[data-live-chart]").onclick = async () => {
      try {
        const id = panel.querySelector("[data-update-id]")?.value?.trim();
        if (!id) throw new Error("Pick sensorId trước");
        const data = await api("GET", `/api/iot/sensor-data/${id}?page=1&pageSize=60`);
        drawSensorHistoryChart(panel, id, data);
        msg(data);
      } catch (e) {
        msg(e.payload || e.message, true);
      }
    };

    if (window.__csLiveTimer) {
      clearInterval(window.__csLiveTimer);
      window.__csLiveTimer = null;
    }
    auto.onchange = () => {
      if (window.__csLiveTimer) {
        clearInterval(window.__csLiveTimer);
        window.__csLiveTimer = null;
      }
      if (auto.checked) {
        window.__csLiveTimer = setInterval(() => {
          if (mod().id !== "iot-live") return;
          loadList(panel);
        }, 15000);
      }
    };
  }

  function refreshLiveCards(panel, rows) {
    const hold = panel.querySelector("[data-live-cards]");
    if (!hold) return;
    const focus = ["Temperature", "pH", "Salinity", "DO", "Level"];
    const byType = {};
    for (const r of rows || []) {
      const t = r.sensorType || "Other";
      if (!byType[t]) byType[t] = [];
      byType[t].push(r);
    }
    const types = focus.concat(Object.keys(byType).filter((t) => !focus.includes(t)));
    hold.innerHTML = types
      .map((t) => {
        const list = byType[t] || [];
        const first = list[0];
        const alarm = list.some((x) => x.alarm && x.alarm !== "ok" && x.alarm !== "none");
        const border = alarm ? "#c00" : first ? "#090" : "#999";
        const val = first
          ? `${first.latestValue ?? "—"} ${first.unit || ""}`
          : "no data";
        const code = first ? first.sensorCode : "";
        return `<div style="border:2px solid ${border};padding:8px;min-width:140px;background:${alarm ? "#fff0f0" : "#f8fff8"}">
          <div style="font-weight:bold">${escapeHtml(t)}</div>
          <div style="font-size:18px">${escapeHtml(String(val))}</div>
          <div style="font-size:11px;opacity:.8">${escapeHtml(code)} · n=${list.length}${alarm ? " · ALARM" : ""}</div>
        </div>`;
      })
      .join("");
  }

  function drawSensorHistoryChart(panel, sensorId, data) {
    const canvas = panel.querySelector("[data-live-chart]");
    if (!canvas) return;
    if (typeof Chart !== "function") {
      msg("Chart.js chưa tải — kiểm tra CDN trong index.html", true);
      return;
    }
    const rows = unwrapList(data).slice().reverse();
    const labels = rows.map(
      (r) => r.measuredAt || r.createdAt || r.timestamp || ""
    );
    const values = rows.map((r) => Number(r.value ?? r.readingValue ?? r.latestValue));
    if (window.__csChart) {
      window.__csChart.destroy();
      window.__csChart = null;
    }
    window.__csChart = new Chart(canvas.getContext("2d"), {
      type: "line",
      data: {
        labels,
        datasets: [
          {
            label: `sensor ${sensorId}`,
            data: values,
            borderColor: "#066",
            tension: 0.2,
            pointRadius: 2,
          },
        ],
      },
      options: {
        responsive: true,
        scales: { x: { ticks: { maxTicksLimit: 8 } } },
      },
    });
  }

  function mountQrScanUpload(panel) {
    if (panel.querySelector("[data-qr-scan-upload]")) return;
    const box = document.createElement("fieldset");
    box.dataset.qrScanUpload = "1";
    box.innerHTML = `
      <legend>Quét QR — tải ảnh lên → trả thông tin hộp/cua</legend>
      <p style="margin:4px 0;opacity:.9">Chọn ảnh tem QR (PNG/JPG). Trình duyệt đọc mã → gọi API <code>GET /api/box-qr/scan</code>.</p>
      <label>Ảnh QR
        <input type="file" accept="image/*" data-qr-file />
      </label>
      <button type="button" data-qr-decode>Đọc QR + Tra cứu</button>
      <span data-qr-decode-status style="margin-left:8px"></span>
      <div style="margin-top:6px">
        <img data-qr-preview-thumb alt="" style="max-width:160px;max-height:160px;border:1px solid #ccc;display:none" />
      </div>
    `;
    // insert after filter fieldset
    const filter = panel.querySelector("fieldset");
    if (filter && filter.nextSibling) panel.insertBefore(box, filter.nextSibling);
    else panel.appendChild(box);

    const fileInp = box.querySelector("[data-qr-file]");
    const status = box.querySelector("[data-qr-decode-status]");
    const thumb = box.querySelector("[data-qr-preview-thumb]");

    fileInp.addEventListener("change", () => {
      const file = fileInp.files?.[0];
      if (!file) {
        thumb.style.display = "none";
        return;
      }
      const url = URL.createObjectURL(file);
      thumb.src = url;
      thumb.style.display = "block";
      status.textContent = file.name;
    });

    box.querySelector("[data-qr-decode]").onclick = async () => {
      try {
        const file = fileInp.files?.[0];
        if (!file) throw new Error("Chọn ảnh QR trước");
        status.textContent = "Đang đọc QR…";
        const code = await decodeQrCodeFromFile(file);
        setComboValue(panel, "code", code, { silent: true });
        status.textContent = `code=${code} → đang gọi scan…`;
        const data = await api("GET", `/api/box-qr/scan?code=${encodeURIComponent(code)}`);
        const boxId = data?.data?.box?.id || data?.data?.Box?.id || data?.data?.qr?.boxId;
        if (boxId) setComboValue(panel, "boxId", boxId);
        const crabs = data?.data?.crabs || data?.data?.Crabs || [];
        if (crabs.length === 1) setComboValue(panel, "crabId", crabs[0].id);
        renderScanResult(panel, data, code);
        msg(data);
        status.textContent = `OK — ${code}`;
      } catch (e) {
        status.textContent = "";
        msg(e.payload || e.message, true);
      }
    };
  }

  function setComboValue(panel, key, value, { silent } = {}) {
    const inp = panel.querySelector(`[data-f="${key}"]`);
    const dd = panel.querySelector(`[data-dd="${key}"]`);
    if (!inp) return;
    const v = value == null ? "" : String(value);
    if (inp.value === v) {
      if (dd) dd.value = v;
      return;
    }
    inp.value = v;
    if (dd) {
      const has = Array.from(dd.options).some((o) => o.value === v);
      dd.value = has ? v : "";
    }
    if (!silent) inp.dispatchEvent(new Event("change", { bubbles: true }));
  }

  function showCrabStatus(panel, crabId) {
    let hold = panel.querySelector("[data-crab-status]");
    if (!hold) {
      hold = document.createElement("fieldset");
      hold.dataset.crabStatus = "1";
      hold.innerHTML = "<legend>Tình trạng cua đã chọn</legend><div data-crab-status-body></div>";
      const boxCrabs = panel.querySelector("[data-box-crabs]");
      if (boxCrabs) panel.insertBefore(hold, boxCrabs);
      else {
        const filterBox = panel.querySelector("fieldset");
        if (filterBox && filterBox.nextSibling) panel.insertBefore(hold, filterBox.nextSibling);
        else panel.appendChild(hold);
      }
    }
    const body = hold.querySelector("[data-crab-status-body]");
    if (!crabId) {
      body.innerHTML = "<i>Chưa chọn crabId</i>";
      return;
    }
    const crab = (refs.crabs || []).find((c) => String(c.id) === String(crabId));
    if (!crab) {
      body.innerHTML = `<p>Không tìm thấy crab trong cache — bấm <b>Reload dropdowns</b>.</p>`;
      return;
    }
    const box = (refs.boxes || []).find((b) => String(b.id) === String(crab.boxId));
    const row = box ? (refs.rows || []).find((r) => String(r.id) === String(box.farmingRowId)) : null;
    const area = row ? (refs.areas || []).find((a) => String(a.id) === String(row.farmingAreaId)) : null;
    body.innerHTML = `<table>
      <tr><th>tag</th><td>${escapeHtml(crab.tag || "(no tag)")}</td></tr>
      <tr><th>moltingStage (tình trạng vỏ)</th><td><b>${escapeHtml(crab.moltingStage || "—")}</b></td></tr>
      <tr><th>weightGram</th><td>${escapeHtml(crab.weightGram ?? "—")}</td></tr>
      <tr><th>alive</th><td>${escapeHtml(crab.isAlive)}</td></tr>
      <tr><th>moltedAt</th><td>${escapeHtml(crab.moltedAt || "—")}</td></tr>
      <tr><th>hộp / dãy / khu</th><td>${escapeHtml(box?.code || "?")} / ${escapeHtml(row?.name || "?")} / ${escapeHtml(area?.name || "?")}</td></tr>
      <tr><th>box status</th><td>${escapeHtml(box?.status || "—")}</td></tr>
    </table>
    <p style="margin:.4rem 0 0;opacity:.85">Khi POST molting: <code>weightAfterGram</code> = trọng lượng sau lột; <code>notes</code> = ghi chú tình trạng cua (khỏe/thương…); stage vỏ sau lột thành công = <b>softshell</b>.</p>`;
  }

  function crabsInBox(boxId) {
    return (refs.crabs || []).filter((c) => String(c.boxId) === String(boxId) && c.isAlive !== false);
  }

  function showBoxCrabs(panel, boxId) {
    let hold = panel.querySelector("[data-box-crabs]");
    if (!hold) {
      hold = document.createElement("fieldset");
      hold.dataset.boxCrabs = "1";
      hold.innerHTML = "<legend>Cua đang trong hộp đã chọn</legend><div data-box-crabs-body></div>";
      const filterBox = panel.querySelector("fieldset");
      if (filterBox && filterBox.nextSibling) panel.insertBefore(hold, filterBox.nextSibling);
      else panel.appendChild(hold);
    }
    const body = hold.querySelector("[data-box-crabs-body]");
    if (!boxId) {
      body.innerHTML = "<i>Chưa chọn boxId</i>";
      return;
    }
    const list = crabsInBox(boxId);
    const box = (refs.boxes || []).find((b) => String(b.id) === String(boxId));
    if (!list.length) {
      body.innerHTML = `<p>Hộp <b>${escapeHtml(box?.code || boxId)}</b>: không có cua alive.</p>`;
      return;
    }
    let html = `<p>Hộp <b>${escapeHtml(box?.code || "")}</b> — ${list.length} cua:</p><table><tr><th>tag</th><th>id</th><th>stage</th><th>weight</th><th>alive</th><th></th></tr>`;
    for (const c of list) {
      html += `<tr>
        <td>${escapeHtml(c.tag || "")}</td>
        <td class="id">${escapeHtml(c.id)}</td>
        <td>${escapeHtml(c.moltingStage || "")}</td>
        <td>${escapeHtml(c.weightGram ?? "")}</td>
        <td>${escapeHtml(c.isAlive)}</td>
        <td><button type="button" data-pick-crab="${escapeAttr(c.id)}">Dùng crabId này</button></td>
      </tr>`;
    }
    html += "</table>";
    body.innerHTML = html;
    body.querySelectorAll("[data-pick-crab]").forEach((btn) => {
      btn.onclick = () => {
        const id = btn.getAttribute("data-pick-crab");
        setComboValue(panel, "crabId", id);
      };
    });
  }

  function fillFromCrab(panel, crabId) {
    showCrabStatus(panel, crabId);
    if (!crabId) return;
    const crab = (refs.crabs || []).find((c) => String(c.id) === String(crabId));
    if (!crab?.boxId) return;
    const box = (refs.boxes || []).find((b) => String(b.id) === String(crab.boxId));
    const row = box ? (refs.rows || []).find((r) => String(r.id) === String(box.farmingRowId)) : null;
    const areaId = row?.farmingAreaId;
    const rowId = box?.farmingRowId;
    const boxId = crab.boxId;

    // Order: area → row (narrow boxes) → box → show crabs in box
    if (areaId) setComboValue(panel, "farmingAreaId", areaId, { silent: true });
    if (areaId) {
      const rowWrap = panel.querySelector(`[data-field-wrap="farmingRowId"]`);
      if (rowWrap) {
        const rows = (refs.rows || []).filter((r) => String(r.farmingAreaId) === String(areaId));
        fillComboOptions(rowWrap, rows, "rows");
      }
    }
    if (rowId) setComboValue(panel, "farmingRowId", rowId, { silent: true });
    if (rowId) {
      const boxWrap = panel.querySelector(`[data-field-wrap="boxId"]`);
      if (boxWrap) {
        const boxes = (refs.boxes || []).filter((b) => String(b.farmingRowId) === String(rowId));
        fillComboOptions(boxWrap, boxes, "boxes");
      }
    }
    setComboValue(panel, "boxId", boxId, { silent: true });
    // sync dropdown selects explicitly
    setComboValue(panel, "farmingAreaId", areaId, { silent: true });
    setComboValue(panel, "farmingRowId", rowId, { silent: true });
    setComboValue(panel, "boxId", boxId, { silent: true });
    showBoxCrabs(panel, boxId);

    // Prefill weight on create form from current crab
    const wInp = panel.querySelector(`[data-form="create"] [data-f="weightAfterGram"]`);
    if (wInp && (crab.weightGram != null || crab.weightGram === 0)) {
      if (!wInp.value) wInp.value = String(crab.weightGram);
    }
  }

  function onIdFieldChanged(panel, inp) {
    const key = inp.getAttribute("data-f");
    if (key === "crabId") {
      fillFromCrab(panel, inp.value.trim());
      if (inp.value.trim() && typeof mod().listPath === "function") {
        loadList(panel).catch(() => {});
      }
      return;
    }
    if (key === "boxId") {
      const boxId = inp.value.trim();
      // narrow crabId dropdown to crabs in this box (alive preferred; else all in box)
      const crabWrap = panel.querySelector(`[data-field-wrap="crabId"]`);
      if (crabWrap) {
        let list = crabsInBox(boxId);
        if (!list.length && boxId) {
          list = (refs.crabs || []).filter((c) => String(c.boxId) === String(boxId));
        }
        fillComboOptions(crabWrap, boxId ? list : refs.crabs || [], "crabs");
      }
      showBoxCrabs(panel, boxId);
      // if exactly 1 alive crab → auto fill crabId (then fillFromCrab runs)
      const alive = crabsInBox(boxId);
      if (alive.length === 1) {
        const crabInp = panel.querySelector(`[data-f="crabId"]`);
        if (crabInp && !crabInp.value) setComboValue(panel, "crabId", alive[0].id);
        else if (crabInp?.value) showCrabStatus(panel, crabInp.value.trim());
      }
      // auto area/row from box
      const box = (refs.boxes || []).find((b) => String(b.id) === String(boxId));
      if (box) {
        const row = (refs.rows || []).find((r) => String(r.id) === String(box.farmingRowId));
        if (row?.farmingAreaId) {
          setComboValue(panel, "farmingAreaId", row.farmingAreaId, { silent: true });
          const rowWrap = panel.querySelector(`[data-field-wrap="farmingRowId"]`);
          if (rowWrap) {
            fillComboOptions(
              rowWrap,
              (refs.rows || []).filter((r) => String(r.farmingAreaId) === String(row.farmingAreaId)),
              "rows"
            );
          }
        }
        if (box.farmingRowId) setComboValue(panel, "farmingRowId", box.farmingRowId, { silent: true });
      }
    }
    if (key === "farmingAreaId") {
      const areaId = inp.value.trim();
      const rowWrap = panel.querySelector(`[data-field-wrap="farmingRowId"]`);
      if (rowWrap) {
        const rows = areaId
          ? (refs.rows || []).filter((r) => String(r.farmingAreaId) === String(areaId))
          : refs.rows || [];
        fillComboOptions(rowWrap, rows, "rows");
      }
    }
    if (key === "farmingRowId") {
      const rowId = inp.value.trim();
      const boxWrap = panel.querySelector(`[data-field-wrap="boxId"]`);
      if (boxWrap) {
        const boxes = rowId
          ? (refs.boxes || []).filter((b) => String(b.farmingRowId) === String(rowId))
          : refs.boxes || [];
        fillComboOptions(boxWrap, boxes, "boxes");
      }
    }
  }

  async function enhancePanelCombos(panel) {
    try {
      await ensureRefs(false);
    } catch (e) {
      msg(e.payload || e.message, true);
      return;
    }
    panel.querySelectorAll("[data-field-wrap]").forEach((wrap) => {
      const kind = wrap.dataset.refKind;
      if (!kind) return;
      let rows = listForKind(kind);
      fillComboOptions(wrap, rows, kind);
      wireCombo(wrap, panel);
    });
    const crabInp = panel.querySelector(`[data-f="crabId"]`);
    if (crabInp?.value.trim()) onIdFieldChanged(panel, crabInp);
    else {
      const boxInp = panel.querySelector(`[data-f="boxId"]`);
      if (boxInp) onIdFieldChanged(panel, boxInp);
    }
  }

  function escapeAttr(s) {
    return String(s).replace(/"/g, "&quot;");
  }

  function escapeHtml(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;");
  }

  function renderTabs() {
    const tabs = $("tabs");
    tabs.innerHTML = "";
    for (const m of MODULES) {
      const b = document.createElement("button");
      b.type = "button";
      b.textContent = m.title;
      b.className = m.id === currentId ? "active" : "";
      b.onclick = () => {
        currentId = m.id;
        renderTabs();
        renderPanel();
      };
      tabs.appendChild(b);
    }
  }

  function mod() {
    return MODULES.find((m) => m.id === currentId);
  }

  function getFilters(container) {
    const m = mod();
    const out = {};
    for (const f of m.filters || []) {
      const v = fieldValue(f, container);
      if (v !== undefined) out[f.key] = v;
    }
    return out;
  }

  function resolveHistoryCrud(m) {
    if (!m.historyEntities) {
      return { entity: "default", update: m.update, remove: m.remove };
    }
    const entity = state[m.id]?.entity;
    if (!entity) {
      return { entity: "(xem)", update: null, remove: null };
    }
    const pack = m.historyEntities[entity];
    return {
      entity,
      update: pack?.update || null,
      remove: pack?.remove || null,
    };
  }

  function detectHistoryEntity(rows) {
    const r = rows?.[0];
    if (!r) return "moltings";
    if (r.moltTime != null) return "moltings";
    if (r.newStatus != null || (r.changedAt != null && ("oldStatus" in r || "reason" in r)))
      return "statusHistory";
    if (r.startTime != null) return "allocations";
    return "moltings";
  }

  async function loadList(panel) {
    const m = mod();
    if (!m.listPath) {
      msg("Module này không có list cố định — dùng nút Extra / filter.");
      return;
    }
    const filters = getFilters(panel);
    try {
      let path;
      if (typeof m.listPath === "function") path = m.listPath(filters);
      else {
        const q = new URLSearchParams();
        Object.entries(filters).forEach(([k, v]) => {
          if (v === undefined || v === null || v === "") return;
          const meta = (m.filters || []).find((f) => f.key === k);
          if (meta?.listIgnore) return;
          q.set(k, String(v));
        });
        path = q.toString() ? `${m.listPath}?${q}` : m.listPath;
      }
      const data = await api("GET", path);
      const rows = unwrapList(data);
      state[m.id] = {
        rows,
        last: data,
        entity: m.historyEntities ? "moltings" : state[m.id]?.entity,
      };
      renderTable(panel);
      if (m.id === "iot-live") refreshLiveCards(panel, rows);
      msg(`OK GET ${path} — ${rows.length} lần ghi (mỗi lần = 1 dòng)`);
    } catch (e) {
      msg(e.payload || e.message, true);
    }
  }

  function rowIdOf(m, r) {
    const key = m.idKey || "id";
    return r?.[key] ?? r?.id ?? "";
  }

  function renderTable(panel) {
    const m = mod();
    const hold = panel.querySelector("[data-table]");
    if (!hold) return;
    const rows = state[m.id]?.rows || [];
    if (!rows.length) {
      hold.innerHTML = "<p>(empty)</p>";
      return;
    }
    const crud = resolveHistoryCrud(m);
    const allCols = Array.from(
      rows.reduce((set, r) => {
        Object.keys(r || {}).forEach((k) => set.add(k));
        return set;
      }, new Set())
    );
    const cols = Array.isArray(m.columns) && m.columns.length
      ? m.columns.filter((c) => allCols.includes(c)).concat(allCols.filter((c) => !m.columns.includes(c)))
      : allCols;
    // Prefer explicit columns only when configured AND entity is moltings
    const showCols =
      Array.isArray(m.columns) && m.columns.length && crud.entity === "moltings"
        ? m.columns.filter((c) => allCols.includes(c))
        : Array.isArray(m.columns) && m.columns.length && !m.historyEntities
          ? m.columns.filter((c) => allCols.includes(c))
          : allCols;
    const inlineFields = (crud.update?.fields || []).filter((f) => f.key !== "_raw");
    const useInline = inlineFields.length > 0;

    let html = "";
    if (m.historyEntities) {
      html += `<p style="opacity:.85">Đang sửa/xóa loại: <b>${escapeHtml(crud.entity)}</b> — Save = PUT, Delete = xóa bản ghi (lỡ nhập).</p>`;
    }
    html += "<table><thead><tr>";
    showCols.forEach((c) => (html += `<th>${escapeHtml(c)}</th>`));
    html += "<th>actions (edit nhanh)</th></tr></thead><tbody>";
    for (const r of rows) {
      const rid = String(rowIdOf(m, r) || "");
      html += `<tr data-rowid="${escapeAttr(rid)}">`;
      for (const c of showCols) {
        const v = r[c];
        const cls = c.toLowerCase().endsWith("id") || c === "id" ? "id" : "";
        html += `<td class="${cls}">${escapeHtml(v == null ? "" : typeof v === "object" ? JSON.stringify(v) : v)}</td>`;
      }
      html += "<td>";
      if (rid) {
        if (useInline) {
          html += `<div data-inline="${escapeAttr(rid)}" style="display:flex;flex-wrap:wrap;gap:4px;align-items:center;max-width:520px">`;
          for (const f of inlineFields) {
            const cur = r[f.key];
            let val = cur == null ? "" : String(cur);
            if (f.type === "datetime" && val) {
              try {
                const d = new Date(val);
                if (!Number.isNaN(d.getTime())) {
                  const pad = (n) => String(n).padStart(2, "0");
                  val = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
                }
              } catch { /* keep */ }
            }
            if (f.type === "bool") {
              html += `<label>${escapeHtml(f.key)}
                <select data-if="${escapeAttr(f.key)}">
                  <option value="true" ${val === "true" ? "selected" : ""}>true</option>
                  <option value="false" ${val === "false" ? "selected" : ""}>false</option>
                </select></label> `;
            } else if (f.type === "select" && Array.isArray(f.options)) {
              const opts = f.options
                .map((o) => {
                  const v = String(o);
                  return `<option value="${escapeAttr(v)}" ${val === v ? "selected" : ""}>${escapeHtml(v)}</option>`;
                })
                .join("");
              html += `<label>${escapeHtml(f.key)}
                <select data-if="${escapeAttr(f.key)}">${opts}</select></label> `;
            } else if (f.type === "datetime") {
              html += `<label>${escapeHtml(f.key)}
                <input type="datetime-local" data-if="${escapeAttr(f.key)}" value="${escapeAttr(val)}" /></label> `;
            } else {
              const size = f.type === "number" ? 6 : 10;
              html += `<label>${escapeHtml(f.key)}
                <input data-if="${escapeAttr(f.key)}" size="${size}" value="${escapeAttr(val)}" /></label> `;
            }
          }
          html += `<button type="button" data-save="${escapeAttr(rid)}">Lưu</button> `;
          html += `</div>`;
        } else {
          html += `<button type="button" data-pick="${escapeAttr(rid)}">Pick id</button> `;
          if (crud.update) html += `<button type="button" data-fill="${escapeAttr(rid)}">Đổ form sửa</button> `;
        }
        if (crud.remove) html += `<button type="button" data-del="${escapeAttr(rid)}">Xóa</button> `;
        for (const a of m.rowActions || []) {
          html += `<button type="button" data-rowact="${escapeAttr(a.label)}" data-rid="${escapeAttr(rid)}">${escapeHtml(a.label)}</button> `;
        }
        if (useInline) {
          html += `<button type="button" data-pick="${escapeAttr(rid)}">Pick id</button>`;
        }
      }
      html += "</td></tr>";
    }
    html += "</tbody></table>";
    hold.innerHTML = html;

    hold.querySelectorAll("[data-pick]").forEach((btn) => {
      btn.onclick = () => {
        const idInp = panel.querySelector("[data-update-id]");
        if (idInp) idInp.value = btn.getAttribute("data-pick");
      };
    });
    hold.querySelectorAll("[data-fill]").forEach((btn) => {
      btn.onclick = () => {
        const id = btn.getAttribute("data-fill");
        const row = rows.find((x) => String(rowIdOf(m, x)) === id);
        const idInp = panel.querySelector("[data-update-id]");
        if (idInp) idInp.value = id;
        if (!row || !crud.update) return;
        for (const f of crud.update.fields) {
          if (f.key === "_raw") continue;
          const el = panel.querySelector(`[data-form="update"] [data-f="${f.key}"]`);
          if (!el || row[f.key] == null) continue;
          el.value = String(row[f.key]);
        }
      };
    });
    hold.querySelectorAll("[data-save]").forEach((btn) => {
      btn.onclick = async () => {
        const id = btn.getAttribute("data-save");
        const box = hold.querySelector(`[data-inline="${CSS.escape(id)}"]`);
        if (!box || !crud.update) return;
        const body = {};
        for (const f of inlineFields) {
          const el = box.querySelector(`[data-if="${f.key}"]`);
          if (!el) continue;
          if (f.type === "bool") body[f.key] = el.value === "true";
          else if (f.type === "number") {
            if (el.value !== "") body[f.key] = Number(el.value);
          } else if (f.type === "datetime") {
            if (el.value !== "") body[f.key] = toIso(el.value);
          } else if (el.value !== "") body[f.key] = el.value;
          else body[f.key] = null;
        }
        const row = rows.find((x) => String(rowIdOf(m, x)) === id);
        if (row) {
          for (const f of inlineFields) {
            if (body[f.key] === undefined && row[f.key] !== undefined) body[f.key] = row[f.key];
          }
        }
        try {
          const method = (crud.update.method || "PUT").toUpperCase();
          const data = await api(method, crud.update.path(id), body);
          msg(data);
          refs.loaded = false;
          if (typeof state[m.id]?.reload === "function") await state[m.id].reload();
          else await loadList(panel);
        } catch (e) {
          msg(e.payload || e.message, true);
        }
      };
    });
    hold.querySelectorAll("[data-del]").forEach((btn) => {
      btn.onclick = async () => {
        const id = btn.getAttribute("data-del");
        if (!confirm(`Delete ${crud.entity} ${id}?`)) return;
        try {
          const data = await api("DELETE", crud.remove(id));
          msg(data);
          refs.loaded = false;
          if (typeof state[m.id]?.reload === "function") await state[m.id].reload();
          else await loadList(panel);
        } catch (e) {
          msg(e.payload || e.message, true);
        }
      };
    });
    hold.querySelectorAll("[data-rowact]").forEach((btn) => {
      btn.onclick = async () => {
        const label = btn.getAttribute("data-rowact");
        const id = btn.getAttribute("data-rid");
        const act = (m.rowActions || []).find((a) => a.label === label);
        if (!act) return;
        try {
          const method = (act.method || "GET").toUpperCase();
          const payload =
            method === "GET"
              ? undefined
              : typeof act.body === "function"
                ? act.body()
                : act.body || {};
          const data = await api(method, act.path(id), payload);
          msg(data);
          if (method !== "GET") await loadList(panel);
        } catch (e) {
          msg(e.payload || e.message, true);
        }
      };
    });
  }

  function renderPanel() {
    const m = mod();
    const panel = $("panel");
    panel.innerHTML = `<h3>${escapeHtml(m.title)}</h3>
      <div class="row">Working id: <input data-update-id size="40" placeholder="Pick id from table" /></div>`;

    const filterBox = document.createElement("fieldset");
    filterBox.innerHTML = "<legend>Filter / List (gõ hoặc dropdown)</legend>";
    for (const f of m.filters || []) filterBox.appendChild(renderField(f));
    const btnLoad = document.createElement("button");
    btnLoad.type = "button";
    btnLoad.textContent = m.listPath ? "Load lịch sử / Refresh" : "Filters only";
    btnLoad.onclick = () => loadList(panel);
    const btnRefs = document.createElement("button");
    btnRefs.type = "button";
    btnRefs.textContent = "Reload dropdowns";
    btnRefs.onclick = async () => {
      await ensureRefs(true);
      await enhancePanelCombos(panel);
      msg("Dropdowns reloaded");
    };
    filterBox.appendChild(btnLoad);
    filterBox.appendChild(btnRefs);
    panel.appendChild(filterBox);

    if (m.id === "qr") mountQrScanUpload(panel);
    if (m.id === "iot-live") mountLiveMonitor(panel);

    if (m.create) {
      const box = document.createElement("fieldset");
      box.dataset.form = "create";
      box.innerHTML = `<legend>${escapeHtml(m.create.legend || "Create (POST)")}</legend>`;
      for (const f of m.create.fields || []) box.appendChild(renderField(f));
      const btn = document.createElement("button");
      btn.type = "button";
      btn.textContent = m.create.buttonLabel || "Create";
      btn.onclick = async () => {
        try {
          if (!m.create.path) throw new Error("Dùng Extra button");
          const filters = getFilters(panel);
          const path =
            typeof m.create.path === "function" ? m.create.path(filters) : m.create.path;
          const body = buildBody(m.create.fields, box, m.create.rawPreferred);
          // attach boxId from filter if present (optional on molting)
          if (filters.boxId && body.boxId == null) body.boxId = filters.boxId;
          const data = await api("POST", path, body);
          msg(data);
          refs.loaded = false;
          await ensureRefs(true);
          const crabId = filters.crabId;
          if (crabId) {
            showCrabStatus(panel, crabId);
            fillFromCrab(panel, crabId);
          }
          if (m.listPath) await loadList(panel);
        } catch (e) {
          msg(e.payload || e.message, true);
        }
      };
      box.appendChild(btn);
      if (m.id === "history") {
        const hint = document.createElement("p");
        hint.style.opacity = "0.85";
        hint.style.margin = "6px 0 0";
        hint.textContent =
          "Mỗi lần bấm Ghi = 1 dòng lịch sử. Cùng ngày đo nhiều lần → nhiều dòng (khác moltTime). from/to ở filter để xem theo khoảng ngày.";
        box.appendChild(hint);
      }
      panel.appendChild(box);
    }

    if (m.update) {
      const box = document.createElement("fieldset");
      box.dataset.form = "update";
      box.innerHTML = "<legend>Update (PUT/PATCH) — hoặc Save trên từng dòng bảng</legend>";
      for (const f of m.update.fields || []) box.appendChild(renderField(f));
      const btn = document.createElement("button");
      btn.type = "button";
      btn.textContent = "Update";
      btn.onclick = async () => {
        try {
          const id = panel.querySelector("[data-update-id]").value.trim();
          if (!id) throw new Error("cần id (ô Working id / Pick id)");
          const crud = resolveHistoryCrud(m);
          const upd = crud.update || m.update;
          const body = buildBody(upd.fields || m.update.fields, box, m.update.rawPreferred);
          const method = (upd.method || m.update.method || "PUT").toUpperCase();
          const data = await api(method, upd.path(id), body);
          msg(data);
          refs.loaded = false;
          if (typeof state[m.id]?.reload === "function") await state[m.id].reload();
          else if (m.listPath) await loadList(panel);
        } catch (e) {
          msg(e.payload || e.message, true);
        }
      };
      box.appendChild(btn);
      panel.appendChild(box);
    }

    if (m.extras?.length) {
      const box = document.createElement("fieldset");
      box.innerHTML = "<legend>Extra actions</legend>";
      // share create raw if present
      const rawArea = panel.querySelector(`[data-form="create"] [data-f="_raw"]`);
      for (const ex of m.extras) {
        const b = document.createElement("button");
        b.type = "button";
        b.textContent = ex.label;
        b.onclick = async () => {
          try {
            const filters = getFilters(panel);
            const id = panel.querySelector("[data-update-id]")?.value?.trim();
            const raw = rawArea?.value;
            const data = await ex.run(api, filters, id, raw, panel);
            const rows = unwrapList(data);
            if (rows.length && typeof rows[0] === "object") {
              const entity = ex.entity
                ? ex.entity
                : m.historyEntities
                  ? null
                  : state[m.id]?.entity;
              state[m.id] = {
                rows,
                last: data,
                entity,
                  reload: ex.entity
                  ? async () => {
                      const again = await ex.run(api, getFilters(panel), id, raw, panel);
                      const r2 = unwrapList(again);
                      state[m.id] = {
                        ...state[m.id],
                        rows: r2,
                        last: again,
                        entity,
                      };
                      renderTable(panel);
                      msg(again);
                    }
                  : undefined,
              };
              renderTable(panel);
            }
            msg(data);
          } catch (e) {
            msg(e.payload || e.message, true);
          }
        };
        box.appendChild(b);
      }
      panel.appendChild(box);
    }

    const tableHold = document.createElement("div");
    tableHold.dataset.table = "1";
    panel.appendChild(tableHold);
    renderTable(panel);

    enhancePanelCombos(panel).then(() => {
      // don't auto-load history until crab chosen (listPath function needs crabId)
      if (typeof m.listPath === "string") loadList(panel);
      else {
        const crabInp = panel.querySelector(`[data-f="crabId"]`);
        if (crabInp?.value?.trim()) loadList(panel);
      }
    });
  }

  async function doLogin() {
    try {
      refs.loaded = false;
      const body = {
        username: $("username").value.trim(),
        password: $("password").value,
      };
      const data = await api("POST", "/api/auth/login", body);
      token = data?.data?.accessToken || "";
      user = data?.data?.user || null;
      if (!token) throw new Error("no accessToken");
      localStorage.setItem("cs_token", token);
      localStorage.setItem("cs_api", baseUrl());
      $("loginInfo").textContent = "OK";
      $("app").style.display = "block";
      $("userLine").textContent = user
        ? ` — ${user.username} / ${user.role} / ${user.id}`
        : "";
      renderTabs();
      renderPanel();
      msg(data);
    } catch (e) {
      msg(e.payload || e.message, true);
    }
  }

  function doLogout() {
    clearSession("logged out");
    msg("logged out");
  }

  $("btnLogin").onclick = doLogin;
  $("btnLogout").onclick = doLogout;
  $("btnMe").onclick = async () => {
    try {
      msg(await api("GET", "/api/auth/me"));
    } catch (e) {
      msg(e.payload || e.message, true);
    }
  };

  const savedApi = localStorage.getItem("cs_api");
  if (savedApi) $("apiBase").value = savedApi;

  async function bootWithCachedToken() {
    if (!token) return;
    try {
      const me = await api("GET", "/api/auth/me");
      user = me?.data || null;
      $("app").style.display = "block";
      $("loginInfo").textContent = "token OK";
      $("userLine").textContent = user
        ? ` — ${user.username} / ${user.role} / ${user.id}`
        : "";
      renderTabs();
      renderPanel();
    } catch {
      clearSession("Token hết hạn — bấm Login lại");
    }
  }

  bootWithCachedToken();
})();
