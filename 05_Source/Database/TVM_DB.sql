/* =========================================================
   Smart Ticketing System - Ticket Vendor Machine (TVM)
   Database script for MS SQL Server 2016+ / LocalDB
   Run:  sqlcmd -S "(localdb)\MSSQLLocalDB" -i TVM_DB.sql
   ========================================================= */
SET NOCOUNT ON;
USE master;
GO
IF DB_ID(N'TVM_DB') IS NOT NULL
BEGIN
    ALTER DATABASE TVM_DB SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE TVM_DB;
END
GO
CREATE DATABASE TVM_DB;
GO
USE TVM_DB;
GO

/* ---------- Reference data ---------- */
CREATE TABLE Station (
    StationId   INT IDENTITY(1,1) PRIMARY KEY,
    Code        NVARCHAR(10)  NOT NULL UNIQUE,
    Name        NVARCHAR(100) NOT NULL,
    Line        NVARCHAR(50)  NOT NULL,
    Mode        NVARCHAR(10)  NOT NULL CHECK (Mode IN (N'MRT', N'Bus')),
    Seq         INT           NOT NULL,          -- order on the line (fare distance)
    IsActive    BIT           NOT NULL DEFAULT 1
);

CREATE TABLE TicketType (
    TicketTypeId  INT PRIMARY KEY,
    Name          NVARCHAR(50) NOT NULL,
    ValidityHours INT NOT NULL,                  -- informational; validity = end of day
    MaxRides      INT NOT NULL                   -- 0 = unlimited
);

CREATE TABLE Fare (
    FareId        INT IDENTITY(1,1) PRIMARY KEY,
    FromStationId INT NOT NULL REFERENCES Station(StationId),
    ToStationId   INT NOT NULL REFERENCES Station(StationId),
    TicketTypeId  INT NOT NULL REFERENCES TicketType(TicketTypeId),
    Price         DECIMAL(12,0) NOT NULL CHECK (Price > 0),
    CONSTRAINT UQ_Fare UNIQUE (FromStationId, ToStationId, TicketTypeId)
);

CREATE TABLE Machine (
    MachineId  NVARCHAR(20) PRIMARY KEY,
    StationId  INT NOT NULL REFERENCES Station(StationId),
    Status     NVARCHAR(20) NOT NULL DEFAULT N'InService'
               CHECK (Status IN (N'InService', N'OutOfService', N'Maintenance'))
);

/* ---------- Transactions ---------- */
CREATE TABLE SalesOrder (
    OrderId       UNIQUEIDENTIFIER PRIMARY KEY,
    MachineId     NVARCHAR(20) NOT NULL REFERENCES Machine(MachineId),
    OriginId      INT NOT NULL REFERENCES Station(StationId),
    DestinationId INT NOT NULL REFERENCES Station(StationId),
    TicketTypeId  INT NOT NULL REFERENCES TicketType(TicketTypeId),
    Quantity      INT NOT NULL CHECK (Quantity BETWEEN 1 AND 10),
    UnitPrice     DECIMAL(12,0) NOT NULL,
    TotalAmount   DECIMAL(12,0) NOT NULL,
    Status        NVARCHAR(20) NOT NULL,
    CreatedAt     DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT CK_Order_Total CHECK (TotalAmount = UnitPrice * Quantity)
);

CREATE TABLE Payment (
    PaymentId  UNIQUEIDENTIFIER PRIMARY KEY,
    OrderId    UNIQUEIDENTIFIER NOT NULL REFERENCES SalesOrder(OrderId),
    Method     NVARCHAR(20) NOT NULL CHECK (Method IN (N'Card', N'BankQr', N'Momo', N'VnPay', N'ZaloPay')),
    Amount     DECIMAL(12,0) NOT NULL,
    Status     NVARCHAR(20) NOT NULL,
    GatewayRef NVARCHAR(50) NULL,
    MaskedPan  NVARCHAR(20) NULL,                -- never the full card number (PCI DSS)
    AuthCode   NVARCHAR(10) NULL,
    CreatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    PaidAt     DATETIME2(0) NULL
);

CREATE TABLE Ticket (
    TicketId   NVARCHAR(20) PRIMARY KEY,
    OrderId    UNIQUEIDENTIFIER NOT NULL REFERENCES SalesOrder(OrderId),
    IssuedAt   DATETIME2(0) NOT NULL,
    ValidUntil DATETIME2(0) NOT NULL,
    Signature  NVARCHAR(64) NOT NULL
);

CREATE TABLE TransactionLog (
    LogId     BIGINT IDENTITY(1,1) PRIMARY KEY,
    SessionId UNIQUEIDENTIFIER NOT NULL,
    MachineId NVARCHAR(20) NOT NULL,
    Step      NVARCHAR(40) NOT NULL,
    Detail    NVARCHAR(400) NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSDATETIME()
);
CREATE INDEX IX_Order_CreatedAt ON SalesOrder(CreatedAt);
GO

/* ---------- Report view ---------- */
CREATE VIEW vw_SalesReport AS
SELECT  o.OrderId, o.CreatedAt, CAST(o.CreatedAt AS DATE) AS SaleDate, o.MachineId,
        so.Name AS Origin, sd.Name AS Destination, tt.Name AS TicketType,
        o.Quantity, o.UnitPrice, o.TotalAmount, o.Status AS OrderStatus,
        p.Method AS PaymentMethod, p.Status AS PaymentStatus, p.GatewayRef
FROM    SalesOrder o
JOIN    Station so ON so.StationId = o.OriginId
JOIN    Station sd ON sd.StationId = o.DestinationId
JOIN    TicketType tt ON tt.TicketTypeId = o.TicketTypeId
-- an order may have several attempts (e.g. declined then approved): report the successful one first
OUTER APPLY (SELECT TOP 1 * FROM Payment p WHERE p.OrderId = o.OrderId
             ORDER BY CASE WHEN p.Status IN (N'Captured', N'Refunded', N'Voided') THEN 0 ELSE 1 END, p.CreatedAt DESC) p;
GO

/* ---------- Seed data ---------- */
INSERT INTO TicketType VALUES (1, N'Single trip', 24, 1), (2, N'Day pass', 24, 0);

INSERT INTO Station (Code, Name, Line, Mode, Seq) VALUES
 (N'L1-01', N'Ben Thanh',               N'MRT Line 1', N'MRT', 1),
 (N'L1-02', N'Opera House',             N'MRT Line 1', N'MRT', 2),
 (N'L1-03', N'Ba Son',                  N'MRT Line 1', N'MRT', 3),
 (N'L1-04', N'Van Thanh',               N'MRT Line 1', N'MRT', 4),
 (N'L1-05', N'Tan Cang',                N'MRT Line 1', N'MRT', 5),
 (N'L1-06', N'Thao Dien',               N'MRT Line 1', N'MRT', 6),
 (N'L1-07', N'An Phu',                  N'MRT Line 1', N'MRT', 7),
 (N'L1-08', N'Rach Chiec',              N'MRT Line 1', N'MRT', 8),
 (N'L1-09', N'Phuoc Long',              N'MRT Line 1', N'MRT', 9),
 (N'L1-10', N'Binh Thai',               N'MRT Line 1', N'MRT', 10),
 (N'L1-11', N'Thu Duc',                 N'MRT Line 1', N'MRT', 11),
 (N'L1-12', N'High-Tech Park',          N'MRT Line 1', N'MRT', 12),
 (N'L1-13', N'National University',     N'MRT Line 1', N'MRT', 13),
 (N'L1-14', N'Suoi Tien Bus Terminal',  N'MRT Line 1', N'MRT', 14),
 (N'B109-1', N'Tan Son Nhat Airport',   N'Bus 109',    N'Bus', 1),
 (N'B152-1', N'Cong Hoa Street',        N'Bus 152',    N'Bus', 1),
 (N'B019-1', N'National University Dorm', N'Bus 19',   N'Bus', 1),
 (N'B056-1', N'Cho Lon Bus Station',    N'Bus 56',     N'Bus', 1);

/* Fare rules (DR-03):
   MRT -> MRT single trip = 6,000 + 1,000 x stops (max 20,000); bus single trip = 7,000 flat;
   Day pass = 40,000 flat. */
INSERT INTO Fare (FromStationId, ToStationId, TicketTypeId, Price)
SELECT a.StationId, b.StationId, t.TicketTypeId,
       CASE WHEN t.TicketTypeId = 2 THEN 40000
            WHEN a.Mode = N'MRT' AND b.Mode = N'MRT'
                 THEN CASE WHEN 6000 + 1000 * ABS(a.Seq - b.Seq) > 20000 THEN 20000
                           ELSE 6000 + 1000 * ABS(a.Seq - b.Seq) END
            ELSE 7000 END
FROM Station a CROSS JOIN Station b CROSS JOIN TicketType t
WHERE a.StationId <> b.StationId;

INSERT INTO Machine (MachineId, StationId) VALUES (N'TVM-BT-01', 1), (N'TVM-BT-02', 1), (N'TVM-TD-01', 11);

/* Sample history so the sales report is not empty (demo data). */
DECLARE @i INT = 0;
WHILE @i < 40
BEGIN
    DECLARE @oid UNIQUEIDENTIFIER = NEWID(),
            @dest INT = 2 + ABS(CHECKSUM(NEWID())) % 17,
            @type INT = 1 + CASE WHEN ABS(CHECKSUM(NEWID())) % 5 = 0 THEN 1 ELSE 0 END,
            @qty  INT = 1 + ABS(CHECKSUM(NEWID())) % 3,
            @when DATETIME2(0) = DATEADD(MINUTE, -ABS(CHECKSUM(NEWID())) % (7 * 24 * 60), SYSDATETIME()),
            @k INT = 1 + ABS(CHECKSUM(NEWID())) % 5,
            @method NVARCHAR(20), @price DECIMAL(12,0);
    SET @method = CHOOSE(@k, N'Card', N'BankQr', N'Momo', N'VnPay', N'ZaloPay');
    SELECT @price = Price FROM Fare WHERE FromStationId = 1 AND ToStationId = @dest AND TicketTypeId = @type;
    INSERT INTO SalesOrder VALUES (@oid, N'TVM-BT-01', 1, @dest, @type, @qty, @price, @price * @qty, N'Completed', @when);
    INSERT INTO Payment (PaymentId, OrderId, Method, Amount, Status, GatewayRef, MaskedPan, AuthCode, CreatedAt, PaidAt)
    VALUES (NEWID(), @oid, @method, @price * @qty, N'Captured',
            CONCAT(UPPER(@method), N'-', ABS(CHECKSUM(NEWID())) % 1000000),
            CASE WHEN @method = N'Card' THEN N'**** **** **** 4242' END,
            CASE WHEN @method = N'Card' THEN RIGHT(CONCAT(N'000000', ABS(CHECKSUM(NEWID())) % 1000000), 6) END,
            @when, @when);
    SET @i += 1;
END
GO
PRINT N'TVM_DB created.';
