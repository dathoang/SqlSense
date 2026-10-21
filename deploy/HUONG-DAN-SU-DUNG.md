# SqlSense cho SSMS 22 — Hướng dẫn cài đặt và sử dụng

SqlSense là extension cho **SQL Server Management Studio 22** (x64): gợi ý T-SQL theo schema, tự động sinh code
(alias, điều kiện JOIN, INSERT/EXEC, mở rộng `SELECT *`, snippet), định dạng SQL an toàn, xuất kết quả ra Excel /
INSERT và tô màu cửa sổ truy vấn theo server/database.

Bản online của tài liệu này (có tìm kiếm): <https://dathoang.github.io/SqlSense/huong-dan/>

---

## 1. Cài đặt

### 1.1 Yêu cầu

| | |
|---|---|
| Hệ điều hành | Windows 10/11 x64 |
| SSMS | **SQL Server Management Studio 22** (bản x64). SSMS 20/21 trở xuống **không** được hỗ trợ |
| Quyền | Không cần quyền Administrator (extension cài theo từng user) |
| Xác thực SQL | Windows Authentication hoặc SQL Server Authentication để có gợi ý theo schema (xem mục 6) |

### 1.2 Cài bằng bộ cài (khuyến nghị)

Tải `SqlSense-<phiên bản>.zip` ở trang
[Releases](https://github.com/dathoang/SqlSense/releases/latest) của GitHub. Trang này cũng có riêng file
`SqlSense-<phiên bản>.vsix` (cài thủ công, mục 1.3) và tài liệu này dạng Word. Bộ cài là thư mục / file zip
`SqlSense-<phiên bản>` gồm:

| File | Dùng để |
|---|---|
| `SqlSense.vsix` | Gói extension |
| `Install.cmd` / `Install.ps1` | Cài hoặc nâng cấp |
| `Uninstall.cmd` / `Uninstall.ps1` | Gỡ cài đặt |
| `SsmsSetup.ps1` | Hàm dùng chung cho hai script trên |
| `HUONG-DAN-SU-DUNG.docx` | Tài liệu này (Word) |
| `LICENSE` | Giấy phép MIT |

Các bước:

1. Giải nén `SqlSense-<phiên bản>.zip` ra một thư mục bất kỳ.
   Nếu Windows chặn file tải về: chuột phải file zip › **Properties** › tick **Unblock** trước khi giải nén.
2. **Đóng hết SSMS.**
3. Nhấp đúp **`Install.cmd`**. Script tự tìm mọi bản SSMS 22 trên máy và cài vào đó.
   Kết thúc thành công sẽ thấy dòng màu xanh `Đã cài SqlSense x.y.z.`
4. Mở SSMS 22 — menu **Tools › SqlSense** xuất hiện là đã cài xong.

Chạy từ PowerShell (tuỳ chọn nâng cao):

```powershell
.\Install.ps1                    # cài vào mọi SSMS 22
.\Install.ps1 -CloseSsms         # tự đóng SSMS đang mở (mất nội dung chưa lưu!)
.\Install.ps1 -InstanceId 04cbd0c1   # chỉ cài vào một instance (InstallationID trong SSMS.isolation.ini)
```

> **Không nhấp đúp trực tiếp file `.vsix`** nếu máy có cài Visual Studio: Windows có thể mở nó bằng VSIX
> Installer của Visual Studio và cài nhầm chỗ. Luôn dùng `Install.cmd`.

### 1.3 Cài thủ công (khi không chạy được script)

```powershell
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" /instanceIds:<InstanceId> "SqlSense.vsix"
```

`<InstanceId>` là giá trị `InstallationID=` trong
`C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.isolation.ini`.

### 1.4 Thiết lập lần đầu

1. **Tắt IntelliSense có sẵn của SSMS** để không bị hai danh sách gợi ý cùng lúc. Mặc định SqlSense tự tắt
   (Tools › Options › SqlSense › General › *Turn off SSMS IntelliSense while SqlSense is enabled*). Nếu vẫn thấy hai
   danh sách, tắt tay ở Tools › Options › Text Editor › Transact-SQL › IntelliSense, rồi mở lại cửa sổ query.
2. Mở một cửa sổ query đã kết nối database, vào **Tools › SqlSense › Show Log** và kiểm tra có dòng
   `SqlSense … loaded` và dòng nạp schema của database.
3. (Tuỳ chọn) Nếu team đang dùng style của SQL Prompt: **Tools › SqlSense › Import SQL Prompt Style...** và chọn
   file `.json` của style đó (xem mục 3.3).

### 1.5 Nâng cấp

Chạy `Install.cmd` của bản mới. Nếu báo *"Phiên bản x.y.z đã được cài rồi"* tức bản trong gói trùng bản đang cài:
chạy `Uninstall.cmd` rồi `Install.cmd`, hoặc xin bản build có số phiên bản cao hơn.

### 1.6 Gỡ cài đặt

Đóng SSMS rồi nhấp đúp **`Uninstall.cmd`**, hoặc trong SSMS: **Extensions › Manage Extensions** › SqlSense ›
Uninstall. Thiết lập, snippet riêng và file màu kết nối (`Documents\SqlSense`) được giữ lại.

---

## 2. Gợi ý và tự hoàn thành (use case chính)

Danh sách gợi ý tự mở khi gõ; **Ctrl+Space** mở bất cứ lúc nào. **Tab/Enter** chọn, **Esc** đóng.
Tìm kiếm mờ: gõ `soh` tìm được `SalesOrderHeader`; khớp đầu từ được xếp trước.

### 2.1 Viết SELECT nhanh

```sql
ssf⇥                     →  SELECT * FROM |
SELECT * FROM cus⇥       →  SELECT * FROM dbo.Customers AS c
```

* Sau `FROM` / `JOIN` / `APPLY` / `UPDATE` / `INSERT INTO`: gợi ý bảng, view, hàm bảng.
* Chọn bảng sẽ tự thêm schema và alias không trùng với alias đã có (`Customers` → `c`).
* Trong danh sách cột / WHERE / ORDER BY: gợi ý cột của các bảng trong câu, hàm, biến, từ khoá.
* Gõ `c.` → chỉ cột của bảng có alias `c`; `dbo.` → các object trong schema; `TenDb.` → các schema.
* Nhận biết CTE, bảng tạm `#t` (`CREATE TABLE #t`, `SELECT … INTO #t`), biến bảng, derived table.

### 2.2 JOIN theo khoá ngoại và cột cùng tên

```sql
SELECT *
FROM dbo.SalesOrderHeader AS soh
    JOIN cus⇥                    →  JOIN dbo.Customers AS c
    ON ⇥                         →  ON soh.CustomerID = c.CustomerID
```

Sau `JOIN … ON` SqlSense đề xuất điều kiện lấy từ foreign key giữa các bảng, xếp đầu danh sách.

Tiếp theo là điều kiện theo **cột cùng tên** ở hai bảng, kể cả khi không có foreign key:

* Cột có dạng khoá (tên kết thúc bằng `ID`, `Code`, `Key`, `No`, `Num`, `Number`) đứng trước; nếu có từ hai cột như vậy
  thì có thêm một gợi ý ghép bằng `AND` (ví dụ `b.CustomerID = a.CustomerID AND b.RegionCode = a.RegionCode`).
* Dùng được cả với CTE, bảng tạm, biến bảng, derived table và khi chưa tải được metadata.
* Bỏ qua cột khác kiểu dữ liệu, và cột identity ở cả hai bảng (hai khoá tự tăng không liên quan).
* Tắt ở Tools › Options › SqlSense › Suggestions › *Suggest JOIN on same-named columns*.

### 2.3 Mở rộng `SELECT *` thành danh sách cột

* Gõ `*` (hoặc `c.*`) rồi nhấn **Tab** ngay sau dấu `*`.
* Hoặc đặt con trỏ trên dòng có `*` và nhấn **Ctrl+B, Ctrl+W** (Tools › SqlSense › Expand Wildcard).

### 2.4 INSERT và EXEC có sẵn khuôn

```sql
INSERT INTO cus⇥
-- →
INSERT INTO dbo.Customers (Name, Email)
VALUES (N'', '')                    -- giá trị mẫu theo kiểu dữ liệu, bỏ qua cột identity/computed

EXEC usp_get⇥
-- →
EXEC dbo.usp_GetOrders @CustomerID = 0, @From = SYSDATETIME()
```

Khi con trỏ nằm trên một cột trong danh sách cột của INSERT, giá trị tương ứng trong `VALUES` được tô sáng (và
ngược lại) — tiện kiểm tra INSERT dài. Màu đổi ở Tools › Options › Environment › Fonts and Colors ›
*SqlSense INSERT column/value*.

### 2.5 Sửa procedure / function / view / trigger có sẵn

Gõ `ALTER PROCEDURE ` (hoặc `ALTER PROC`, `ALTER FUNCTION`, `ALTER VIEW`, `ALTER TRIGGER`) — danh sách chỉ hiện các
object **đúng loại** đó trong database. Chọn một object trong cửa sổ query **trống** (hoặc batch trống sau `GO`):

```sql
ALTER PROCEDURE usp_get⇥
-- → sau giây lát, toàn bộ câu lệnh được thay bằng định nghĩa hiện tại của procedure:
-- header comment
ALTER PROCEDURE dbo.usp_GetOrders @CustomerID int, @From date = NULL
AS
SELECT …
```

* Giống *Script as ALTER* của Object Explorer nhưng không cần rời bàn phím: comment đầu và thân giữ nguyên từng ký tự,
  chỉ `CREATE` được đổi thành `ALTER`.
* `CREATE OR ALTER PROCEDURE ` + chọn object → script dạng `CREATE OR ALTER`.
* Định nghĩa được tải ở background nên không làm đứng editor; thanh trạng thái báo *scripting…* / *scripted*.
  Nếu bạn gõ tiếp trước khi tải xong, SqlSense không chèn đè. **Ctrl+Z** quay về chỉ còn tên object.
* Nếu sau tên đã có thân (`AS …`), SqlSense chỉ thay tên, không chèn thêm định nghĩa.
* Object CLR, `WITH ENCRYPTION` hoặc thiếu quyền `VIEW DEFINITION` → chỉ chèn tên, thanh trạng thái báo lý do.
* `DROP PROCEDURE ` / `DROP TRIGGER IF EXISTS ` … cũng gợi ý đúng loại object (chỉ chèn tên).
* `CREATE TRIGGER trg ON ` gợi ý bảng và view.
* Tắt ở Tools › Options › SqlSense › Suggestions › *ALTER scripts the object's definition*.

### 2.6 Snippet

Gõ phím tắt rồi nhấn **Tab**:

| Phím tắt | Sinh ra | Phím tắt | Sinh ra |
|---|---|---|---|
| `ssf` | `SELECT * FROM` | `bt` | Transaction + TRY/CATCH + ROLLBACK |
| `st100` | `SELECT TOP 100 * FROM` | `tc` | `BEGIN TRY … END CATCH` |
| `scf` | `SELECT COUNT(*) FROM` | `ifex` | `IF EXISTS (SELECT 1 FROM …)` |
| `sdf` | `SELECT DISTINCT … FROM` | `cte` | `WITH cte AS (…)` |
| `ii` / `iis` | `INSERT INTO` / `INSERT … SELECT` | `cur` | Vòng lặp cursor |
| `uu` | `UPDATE … SET` | `cp` / `cv` | `CREATE OR ALTER PROCEDURE / VIEW` |
| `df` | `DELETE FROM` | `ct` / `tt` | `CREATE TABLE` / bảng tạm `#` |
| `ij` / `lj` | `INNER / LEFT JOIN … ON` | `dt` | `DROP TABLE IF EXISTS` |
| `w2` | `WHERE … = ` | `sn` | `SET NOCOUNT ON;` |
| `ob` / `gb` | `ORDER BY` / `GROUP BY` | `rn` | `ROW_NUMBER() OVER (…)` |
| `be` | `BEGIN … END` | `sp_who` | Các session đang chạy, đang block |

**Snippet riêng của bạn / của team:** tạo file `<phím tắt>.sql` trong `Documents\SqlSense\Snippets`
(hoặc thư mục đặt ở Tools › Options › SqlSense › General › *Snippets folder* — có thể trỏ tới thư mục dùng chung).
Dòng đầu `-- mô tả` là mô tả hiển thị; `$CURSOR$` đánh dấu vị trí con trỏ sau khi chèn. Ví dụ `audit.sql`:

```sql
-- Lịch sử thay đổi của một bản ghi
SELECT *
FROM dbo.AuditLog
WHERE TableName = N'$CURSOR$'
ORDER BY ChangedAt DESC;
```

### 2.7 Khác

* Gõ `(`, `[`, `'` tự thêm dấu đóng; gõ dấu đóng sẽ đè lên dấu có sẵn.
* `DECLARE @x ` / `CAST(… AS ` gợi ý kiểu dữ liệu; `USE ` gợi ý database; `@` gợi ý biến đã khai báo.
* `USE TenDb` trong script đổi database dùng cho gợi ý ở các câu bên dưới.

---

## 3. Định dạng SQL

### 3.1 Dùng

* **Ctrl+K, Ctrl+Y** — định dạng vùng chọn, hoặc cả tài liệu nếu không chọn gì.
* Hoặc Tools › SqlSense › Format SQL; Edit › Advanced › Format Document / Format Selection.

```sql
select c.CustomerID, c.Name, count(*) as n from dbo.Customers c inner join sales.Orders o on o.CustomerID = c.CustomerID and o.Total > 0 where c.Active = 1 group by c.CustomerID, c.Name order by n desc
```

thành (style mặc định)

```sql
SELECT c.CustomerID,
       c.Name,
       COUNT(*) AS n
FROM dbo.Customers c
    INNER JOIN sales.Orders o
        ON o.CustomerID = c.CustomerID
           AND o.Total > 0
WHERE c.Active = 1
GROUP BY c.CustomerID,
         c.Name
ORDER BY n DESC
```

### 3.2 An toàn

Sau khi định dạng, SqlSense so sánh lại toàn bộ token với bản gốc. Nếu có bất kỳ thay đổi nào ngoài khoảng trắng
và chữ hoa/thường của từ khoá, **không có gì bị thay đổi** và lý do hiện ở thanh trạng thái. Comment và dòng trống
giữa các câu lệnh được giữ nguyên. Luôn có thể **Ctrl+Z** để hoàn tác.

### 3.3 Tuỳ chỉnh style

Tools › Options › **SqlSense › Formatting**: kích thước thụt lề, chữ hoa/thường cho keyword / hàm / kiểu dữ liệu /
`@@ROWCOUNT`, dấu phẩy đầu dòng, mỗi cột một dòng, xuống dòng trước `AND`/`OR`, thụt JOIN, vị trí `WHEN` đầu tiên,
kiểu ngoặc của subquery và `CREATE TABLE`, căn alias / comment, giữ câu ngắn trên một dòng (*Collapse short code*), …

**Thống nhất style cho cả team:** export style từ SQL Prompt (file `.json`) rồi mỗi người chạy
**Tools › SqlSense › Import SQL Prompt Style...**. Hộp thoại sau khi import liệt kê số thiết lập đã áp dụng và
những thiết lập chưa có tương đương; chi tiết ở Show Log.

---

## 4. Làm việc với kết quả truy vấn

Chuột phải trên lưới kết quả (Results grid):

| Lệnh | Tác dụng |
|---|---|
| **Open Results in Excel** | Lưu lưới kết quả thành file `.xlsx` (giữ kiểu số/ngày) và mở bằng Excel. Tối đa 1.048.575 dòng (giới hạn Excel); nếu máy không có ứng dụng mở `.xlsx`, đường dẫn file hiện ở thanh trạng thái |
| **Script Results as INSERT** | Sinh các câu `INSERT … VALUES` từ kết quả, mở trong cửa sổ query mới (không mở được thì copy vào clipboard). Tên bảng đích được đoán từ câu SELECT đã chạy |

Nếu chọn nhiều ô trước khi bấm, chỉ các ô đã chọn được xuất. Dùng để: chuyển dữ liệu mẫu giữa môi trường,
tạo script seed/test data, gửi số liệu cho người không dùng SSMS.

---

## 5. Tô màu cửa sổ theo kết nối (tránh chạy nhầm PROD)

Mỗi cửa sổ query được tô màu tab, khung editor và thanh trạng thái theo server/database đang kết nối.

### 5.1 Đặt màu nhanh từ tab

Chuột phải lên **tab** cửa sổ query › **Connection Color**:

* **Db ›** chọn màu — áp dụng cho đúng *server/database* này.
* **Server ›** chọn màu — áp dụng cho mọi database trên server này.
* **No Color** — không tô (ví dụ loại `tempdb` khỏi màu của server), **Custom...** — chọn màu tuỳ ý,
  **Remove Rule** — xoá quy tắc.

### 5.2 File quy tắc

**Tools › SqlSense › Edit Connection Colors...** mở `Documents\SqlSense\ConnectionColors.txt`. Lưu file là áp dụng ngay:

```text
# server[/database] = màu
PROD-SQL01            = Red
PROD-SQL01/Reporting  = Orange
PROD-SQL01/tempdb     = none
*.test.contoso.com    = Green
localhost\*/*_Staging = #8E44AD
```

* `*` và `?` là ký tự đại diện, không phân biệt hoa/thường.
* Quy tắc có database thắng quy tắc chỉ có server; còn lại dòng khớp đầu tiên thắng.
* Màu: `#RRGGBB`, `#AARRGGBB`, tên (Red, DarkRed, Orange, Yellow, Gold, Green, DarkGreen, Teal, Cyan, Blue, Navy,
  Purple, Magenta, Pink, Brown, Gray, Black, White) hoặc `none`.
* Cửa sổ không khớp quy tắc nào dùng màu chọn trong hộp thoại Connect của SSMS (Options › *Use custom color*).
* Có thể đặt file ở thư mục dùng chung để cả team cùng một bộ màu: Tools › Options › SqlSense ›
  Connection Colors › *Connection colours file*.

Menu *Set Tab Color* có sẵn của SSMS được ẩn đi vì nó lưu màu theo file và sẽ xung đột (bật lại ở Options nếu cần).

---

## 6. Thiết lập và xử lý sự cố

### 6.1 Menu Tools › SqlSense

| Lệnh | Khi nào dùng |
|---|---|
| Format SQL (Ctrl+K, Ctrl+Y) | Định dạng |
| Expand Wildcard (Ctrl+B, Ctrl+W) | Mở rộng `SELECT *` |
| **Refresh Suggestions** | Vừa tạo/sửa bảng, cột, procedure, trigger mà gợi ý chưa thấy (schema được cache 30 phút) |
| Disable / Enable SqlSense | Tạm tắt toàn bộ SqlSense |
| **Show Log** | Xem chẩn đoán (Output pane "SqlSense") |
| Import SQL Prompt Style... | Nhập style định dạng |
| Edit Connection Colors... | Sửa quy tắc màu |

Toàn bộ tuỳ chọn: **Tools › Options › SqlSense** (General, Suggestions, Formatting, Connection Colors).

### 6.2 Sự cố thường gặp

| Hiện tượng | Nguyên nhân / cách xử lý |
|---|---|
| Không có menu Tools › SqlSense | Cài vào sai instance hoặc SSMS mở khi đang cài. Đóng SSMS, chạy lại `Install.cmd`; kiểm tra Extensions › Manage Extensions |
| Hai danh sách gợi ý cùng hiện | Tắt IntelliSense của SSMS (mục 1.4) rồi mở lại cửa sổ query |
| Chỉ gợi ý từ khoá/hàm, không có bảng/cột | Đăng nhập bằng **Microsoft Entra ID** (chưa hỗ trợ — dùng Windows/SQL auth), SSMS không cung cấp mật khẩu SQL login (tick *Remember password*), hoặc schema đang nạp lần đầu (database lớn mất vài giây). Xem Show Log |
| Thiếu bảng/cột vừa tạo | Tools › SqlSense › Refresh Suggestions |
| Đổi database ở dropdown nhưng gợi ý vẫn của database cũ | Thêm `USE TenDb` ở đầu script, hoặc Refresh Suggestions |
| Format không làm gì | Xem thanh trạng thái: formatter từ chối vì kết quả sẽ thay đổi token (SQL lỗi cú pháp nặng thường gây ra). Gửi đoạn SQL đó cho nhóm phát triển |
| Cài báo lỗi | Xem log VSIXInstaller ở đường dẫn script in ra (`%TEMP%\SqlSense_install_*.log`) |

Khi báo lỗi, gửi kèm nội dung **Show Log**, phiên bản SqlSense (Extensions › Manage Extensions) và phiên bản SSMS
(Help › About).

---

## 7. Dành cho người build bộ cài

Yêu cầu: Visual Studio 2022/2026 với workload **Visual Studio extension development**, .NET SDK và Node.js 22+
(để sinh tài liệu Word từ `deploy\HUONG-DAN-SU-DUNG.md`; lần đầu script tự chạy `npm ci` trong `deploy\tools`).

```powershell
.\deploy\Build-Package.ps1 -Version 1.3.0           # test + rebuild + đóng gói
.\deploy\Build-Package.ps1 -Version 1.3.0 -Install  # ... và cài luôn vào SSMS trên máy này
.\deploy\Build-Package.ps1 -SkipTests               # giữ nguyên phiên bản, bỏ qua unit test
```

Kết quả: `dist\SqlSense-<version>\` và `dist\SqlSense-<version>.zip` (VSIX + script cài/gỡ + tài liệu Word +
LICENSE), cùng `dist\SqlSense-<version>.vsix` và `dist\HUONG-DAN-SU-DUNG-<version>.docx` để đính kèm riêng lên
GitHub Release; `dist\SqlSense.vsix` luôn là bản build mới nhất. **Phải tăng `-Version`** mỗi lần phát hành, nếu không máy đã cài
bản cũ sẽ từ chối cài đè.
