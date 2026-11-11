-- Read-only audit for legacy IoT links. Run on a test database first.
-- No row is changed; every returned row requires manual review.
SELECT
    s."Id" AS "SensorId",
    s."SensorCode",
    s."WaterSystemId",
    ws."FarmingAreaId" AS "WaterSystemAreaId",
    s."DeviceId",
    COALESCE(d."FarmingAreaId", dr."FarmingAreaId") AS "DeviceAreaId",
    s."FarmingRowId",
    sr."FarmingAreaId" AS "SensorRowAreaId",
    CASE
        WHEN ws."FarmingAreaId" IS NOT NULL
             AND COALESCE(d."FarmingAreaId", dr."FarmingAreaId") IS NOT NULL
             AND ws."FarmingAreaId" <> COALESCE(d."FarmingAreaId", dr."FarmingAreaId")
            THEN 'WATER_SYSTEM_DEVICE_AREA_MISMATCH'
        WHEN ws."FarmingAreaId" IS NOT NULL
             AND sr."FarmingAreaId" IS NOT NULL
             AND ws."FarmingAreaId" <> sr."FarmingAreaId"
            THEN 'WATER_SYSTEM_SENSOR_ROW_AREA_MISMATCH'
        WHEN COALESCE(d."FarmingAreaId", dr."FarmingAreaId") IS NOT NULL
             AND sr."FarmingAreaId" IS NOT NULL
             AND COALESCE(d."FarmingAreaId", dr."FarmingAreaId") <> sr."FarmingAreaId"
            THEN 'DEVICE_SENSOR_ROW_AREA_MISMATCH'
        ELSE 'UNRESOLVED_AREA'
    END AS "ReviewReason"
FROM be."Sensors" s
LEFT JOIN be."WaterSystems" ws ON ws."Id" = s."WaterSystemId"
LEFT JOIN be."Devices" d ON d."Id" = s."DeviceId"
LEFT JOIN be."FarmingRows" dr ON dr."Id" = d."FarmingRowId"
LEFT JOIN be."FarmingRows" sr ON sr."Id" = s."FarmingRowId"
WHERE
    (ws."FarmingAreaId" IS NOT NULL
     AND COALESCE(d."FarmingAreaId", dr."FarmingAreaId") IS NOT NULL
     AND ws."FarmingAreaId" <> COALESCE(d."FarmingAreaId", dr."FarmingAreaId"))
 OR (ws."FarmingAreaId" IS NOT NULL
     AND sr."FarmingAreaId" IS NOT NULL
     AND ws."FarmingAreaId" <> sr."FarmingAreaId")
 OR (COALESCE(d."FarmingAreaId", dr."FarmingAreaId") IS NOT NULL
     AND sr."FarmingAreaId" IS NOT NULL
     AND COALESCE(d."FarmingAreaId", dr."FarmingAreaId") <> sr."FarmingAreaId")
 OR (ws."FarmingAreaId" IS NULL
     AND COALESCE(d."FarmingAreaId", dr."FarmingAreaId") IS NULL
     AND sr."FarmingAreaId" IS NULL)
ORDER BY s."SensorCode";
