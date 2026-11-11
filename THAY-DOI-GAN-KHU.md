# Thay đổi gán khu và mã tài khoản

Đã áp dụng trên Supabase, schema `be`. Ba migration mới:

- `20261111082623_SplitUserAssignmentsAndUserCode`
- `20261111085052_OneFarmAdministratorPerArea`
- `20261111090045_DropFarmingAreaOwnerId`

## Mô hình khu

Một Farm Administrator quản lý nhiều khu. Mỗi khu chỉ có một Farm Administrator. Mỗi Farm Manager chỉ có một khu. Mỗi Farm Staff thuộc một manager và cùng khu với manager đó.

| Bảng | Việc làm |
|---|---|
| `FarmingAreaAdministrators` | Nối Farm Administrator với khu. `FarmingAreaId` là duy nhất. |
| `FarmManagerAssignments` | Mỗi manager một khu, kèm người đã gán. |
| `FarmStaffAssignments` | Mỗi staff một manager và cùng khu với manager đó. |

`AppUsers.FarmingAreaId` đã bị xóa. Dữ liệu manager được chuyển sang `FarmManagerAssignments`. Staff chỉ được chuyển khi khu đó đã có manager.

## UserCode

`EmployeeId` đổi thành `UserCode`. Hệ thống sinh mã, client không sửa. Đổi role thì cấp mã mới. `Id` uuid vẫn là khóa chính.

| Role | Mã | Sequence |
|---|---|---|
| System Administrator | `SAD-00001` | `be.user_code_sad_seq` |
| Farm Administrator | `FAD-00001` | `be.user_code_fad_seq` |
| Farm Manager | `FMN-00001` | `be.user_code_fmn_seq` |
| Farm Staff | `FST-00001` | `be.user_code_fst_seq` |

User cũ được đánh số lại theo role, thứ tự `CreatedAt` rồi `Id`. Sequence `employee_id_sequence` đã bị xóa.

## Xóa OwnerId của khu

`FarmingAreas.OwnerId` không còn là ranh giới quyền. Trước khi xóa cột, migration copy người tạo sang `FarmingAreaAdministrators` khi:

- người tạo là `FarmAdministrator` hoặc `FarmOwner`
- khu đó chưa có dòng gán

Khu đã có admin thì giữ nguyên. Khu do Manager hoặc Staff tạo mà chưa có dòng gán thì không còn Farm Administrator.

Đã xóa khóa ngoại `FK_FarmingAreas_AppUsers_OwnerId`, index `IX_FarmingAreas_OwnerId` và cột `OwnerId`. Navigation `OwnedAreas` cũng bị xóa.

API khu vẫn trả `ownerId` và `ownerName`. Hai field này là Farm Administrator duy nhất của khu. `Guid.Empty` nghĩa là khu chưa được gán. Bộ lọc `ownerId` lọc theo admin đó.

Tạo khu chỉ dành cho Farm Administrator. Người tạo được ghi vào `FarmingAreaAdministrators`.

## Code đã chuyển sang bảng gán

- Danh sách, chi tiết, cập nhật khu và bản đồ khu.
- Thông báo theo khu: Farm Administrator chỉ nhận khu mình được gán.
- Nhật ký gán hoặc chuyển cua, và nhật ký thêm cua vào hộp, ghi user là admin của khu.
- Dữ liệu demo và khu demo của lịch công việc.
- Đăng ký tài khoản: khu đã có admin thì không gán thêm Farm Administrator. Tạo staff bắt buộc có manager của đúng khu đó.

Tra cứu admin nằm ở `AreaAdministratorQuery`. `ScheduledFarmTasks.OwnerId` giữ nguyên: đó là người sở hữu công việc, không phải chủ khu.

## Việc cần làm phía máy đang chạy

Khởi động lại API nếu process cũ vẫn đang giữ bản build trước khi xóa `OwnerId`.
