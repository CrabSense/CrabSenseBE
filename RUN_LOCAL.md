# Chạy CrabSenseBE local

## Yêu cầu

- .NET SDK 8
- PostgreSQL 14+ hoặc Docker Desktop

## Cách 1: chạy bằng Docker

```powershell
docker compose up -d
dotnet run --project src/CrabSenseBE.Api --no-launch-profile --urls http://localhost:5080
```

## Cách 2: dùng PostgreSQL local

1. Copy `.env.example` thành `.env` và điền chuỗi kết nối, JWT secret và các thông tin dịch vụ.
2. Tạo database PostgreSQL theo schema/seed của nhóm.
3. Chạy:

```powershell
dotnet restore CrabSenseBE.sln
dotnet run --project src/CrabSenseBE.Api --no-launch-profile --urls http://localhost:5080
```

Swagger: http://localhost:5080/swagger

Không gửi thư mục `secrets` hoặc file `.env` qua chat. Hãy tạo secret riêng trên máy người chạy.
