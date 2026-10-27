# SqlSense cho SSMS 22 — Hướng dẫn cài đặt và sử dụng

SqlSense là extension cho **SQL Server Management Studio 22** (x64): gợi ý T-SQL theo schema, tự động sinh code
(alias, điều kiện JOIN, INSERT/EXEC, mở rộng `SELECT *`, snippet), định dạng SQL an toàn, gập code theo cấp như
VS Code, xuất kết quả ra Excel /
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
| Xác thực SQL | Windows Authentication hoặc SQL Server Authentication để có gợi ý theo schema (xem mục 7) |

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

**Tên đầy đủ sang database khác và linked server:**

```sql
SELECT * FROM ⇥                       -- sau bảng/view là các database và linked server
SELECT * FROM Archive.⇥               -- schema của database Archive
SELECT * FROM Archive.hist.⇥          -- bảng/view trong Archive.hist (Archive.. = schema mặc định dbo)
SELECT * FROM REMOTE.⇥                -- database trên linked server REMOTE
SELECT * FROM REMOTE.Sales2.dbo.⇥     -- bảng/view của REMOTE.Sales2.dbo
EXEC Archive.dbo.⇥                    -- procedure của database khác
```

* Alias của các bảng này (`FROM Archive.hist.OldOrders AS o` rồi `o.`) cũng gợi ý cột; rê chuột lên từng phần
  (server, database, schema, bảng, cột) đều có tooltip.
* Lần đầu nhắc tới một database/linked server, metadata của nó được tải ở background; danh sách tự mở lại khi tải xong.
* Cần quyền truy cập database đó; linked server phải là SQL Server và cho phép truy vấn dữ liệu (data access).

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

### 2.6 Phân quyền: user, role, login, schema

Khi viết câu lệnh bảo mật, danh sách gợi ý đúng loại đối tượng đang có trên server/database:

```sql
ALTER ROLE ⇥                              -- role của database (role của bạn đứng trước db_datareader, db_owner …)
ALTER ROLE app_readers ADD MEMBER ⇥       -- user và role của database
ALTER SERVER ROLE sysadmin ADD MEMBER ⇥   -- login và server role
GRANT SELECT ON ⇥                         -- bảng, view, procedure, function, schema (không có trigger)
GRANT SELECT ON dbo.Customers TO ⇥        -- user, role, application role (sau dấu phẩy cũng vậy)
REVOKE EXECUTE ON SCHEMA::sales FROM ⇥
```

* `ALTER` / `DROP [IF EXISTS]` + `ROLE`, `USER`, `LOGIN`, `APPLICATION ROLE`, `SERVER ROLE`, `SCHEMA`, `DATABASE`
  gợi ý đối tượng có sẵn cùng loại; `CREATE ROLE ` / `CREATE USER ` … là tên mới nên không gợi ý.
* `CREATE USER u FOR LOGIN `, `WITH LOGIN = ` → login; `WITH DEFAULT_SCHEMA = ` → schema;
  `WITH DEFAULT_DATABASE = ` → database; `CREATE SCHEMA s AUTHORIZATION ` và `ALTER AUTHORIZATION ON … TO ` → user/role.
* `ON SCHEMA::`, `ROLE::`, `USER::`, `LOGIN::`, `DATABASE::`, `OBJECT::` gợi ý theo đúng loại sau `::`.
* Quyền cấp server (`GRANT VIEW SERVER STATE TO `, `ALTER ANY LOGIN`, `ON LOGIN::` …) gợi ý login và server role.
* Mô tả bên cạnh mỗi mục cho biết loại (`SQL_USER`, `WINDOWS_GROUP` …), thành viên của role, role mà user thuộc về.
  Tên có ký tự đặc biệt (`CORP\alice`) được chèn trong `[ ]`.
* Danh sách login và server role chỉ có khi tài khoản đang kết nối được xem chúng (quyền mặc định thấy chính mình và
  các server role); thiếu quyền thì phần đó trống, các gợi ý khác vẫn chạy.

### 2.7 Xem thông tin khi rê chuột

Để chuột lên một tên trong script, một tooltip cho biết đó là gì:

| Rê chuột lên | Tooltip hiện |
|---|---|
| Bảng, view, alias (`c` trong `c.Name`) | Tên đầy đủ, các cột với kiểu dữ liệu, null/not null, identity; khoá ngoại của bảng |
| Cột (`Name`, `c.Name`, `dbo.Customers.Name`) | Bảng chứa cột, kiểu dữ liệu, null/not null |
| Procedure, function | Danh sách tham số (kiểu, `OUTPUT`, có giá trị mặc định); cột trả về của table-valued function |
| Trigger | Bảng/view mà trigger gắn vào |
| CTE, bảng tạm `#t`, biến bảng | Danh sách cột |
| Biến `@x` | Kiểu đã khai báo; `@@ROWCOUNT` … và hàm có sẵn (`ISNULL`, `LEFT` …) hiện cú pháp và mô tả |
| Schema | Số bảng/view/procedure… trong schema |
| Database | Trạng thái (nếu không ONLINE), recovery model, compatibility level, các file của database hiện tại, lần backup full/differential/log gần nhất |
| Index (`ALTER INDEX ix ON …`) | Loại index, cột khoá, cột `INCLUDE`, bảng; tooltip của bảng cũng liệt kê các index |
| User, role, login | Loại, login tương ứng, schema/database mặc định, thành viên của role, role mà user thuộc về |

* Không cần kết nối cho biến, CTE, bảng tạm và hàm có sẵn; thông tin object cần metadata đã tải (như gợi ý).
* Tắt ở Tools › Options › SqlSense › General › *Show object info on hover*. Nếu IntelliSense của SSMS đang bật, có thể
  thấy thêm tooltip của SSMS.

### 2.8 Backup, restore và bảo trì

```sql
BACKUP DATABASE ⇥                         -- database trên server
BACKUP DATABASE Shop TO ⇥                 -- DISK = N'Shop_20261023_1405.bak' (tên có ngày giờ), DISK = N'', URL = N''
BACKUP DATABASE Shop TO DISK = N'…' WITH ⇥  -- COMPRESSION, COPY_ONLY, DIFFERENTIAL, CHECKSUM, INIT, STATS = 10 …
RESTORE DATABASE Shop FROM ⇥              -- các file backup gần nhất của Shop (lịch sử trong msdb)
RESTORE DATABASE Shop FROM DISK = N'…' WITH ⇥  -- RECOVERY, NORECOVERY, REPLACE, MOVE N'' TO N'', STATS = 10 …
```

* `BACKUP ` / `RESTORE ` gợi ý `DATABASE`, `LOG`, `FILELISTONLY FROM`, `HEADERONLY FROM`, `VERIFYONLY FROM` …
* `BACKUP LOG Shop TO ` gợi ý file `.trn`. Tên file không có thư mục thì SQL Server ghi vào/đọc từ thư mục backup mặc
  định của server.
* `RESTORE DATABASE ` gợi ý cả database đang `RESTORING` (sau `NORECOVERY`). File backup gần nhất chỉ có khi tài khoản đọc
  được `msdb`; mô tả cho biết loại backup và thời điểm.
* `DBCC ` gợi ý các lệnh (`CHECKDB`, `CHECKTABLE`, `SHRINKFILE`, `SQLPERF(LOGSPACE)`, `FREEPROCCACHE` …);
  `DBCC CHECKDB (` → database; `DBCC CHECKIDENT (` / `CHECKTABLE (` → bảng, chèn dạng chuỗi `'dbo.Customers'`;
  `DBCC SHRINKFILE (` → file của database (`N'Shop_log'`); `… WITH ` → `NO_INFOMSGS`, `PHYSICAL_ONLY` …
* `ALTER DATABASE Shop ` gợi ý `SET …`, `MODIFY FILE`, `ADD FILE`, `MODIFY NAME`; `SET ` gợi ý `RECOVERY SIMPLE`,
  `SINGLE_USER WITH ROLLBACK IMMEDIATE`, `COMPATIBILITY_LEVEL = 160`, `READ_COMMITTED_SNAPSHOT ON`, `QUERY_STORE = ON` …;
  `MODIFY FILE (NAME = ` gợi ý file.
* Index: `ALTER INDEX ` gợi ý `ALL ON` và các index (chọn một index chèn luôn `IX_… ON dbo.Bang`); `DROP INDEX ` cũng vậy;
  `ALTER INDEX … ON dbo.Bang ` → `REBUILD`, `REBUILD WITH (ONLINE = ON)`, `REORGANIZE`, `DISABLE` …;
  `CREATE INDEX ix ON ` → bảng, `(` và `INCLUDE (` → cột của bảng đó.
* `UPDATE STATISTICS ` → bảng, sau tên bảng → `WITH FULLSCAN`, `WITH SAMPLE 25 PERCENT` …
* Snippet: `bakd` (backup full), `bakl` (backup log), `rsdb` (restore), `chkdb` (DBCC CHECKDB) — xem 2.9.

### 2.9 Snippet

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
| `bakd` / `bakl` | `BACKUP DATABASE / LOG … WITH COMPRESSION` | `rsdb` | `RESTORE DATABASE … WITH REPLACE` |
| `chkdb` | `DBCC CHECKDB (…) WITH NO_INFOMSGS` | | |

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

### 2.10 Khác

* Gõ `(`, `[`, `'` tự thêm dấu đóng; gõ dấu đóng sẽ đè lên dấu có sẵn.
* Chọn gợi ý khi con trỏ đang ở giữa một từ chỉ chèn tại con trỏ (thay phần đã gõ), phần chữ phía sau con trỏ được giữ nguyên.
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

## 4. Đóng mở code (folding)

Script dài có thể gập lại theo khối, giống VS Code. Bên trái số dòng có **mũi tên** (chevron) ở đầu mỗi vùng gập
được; bấm vào để gập/mở. Rê chuột lên phần đã gập để xem nội dung bên trong.

Vùng gập được:

| Vùng | Ví dụ |
|---|---|
| Khối `BEGIN … END`, `BEGIN TRY … END TRY`, `BEGIN CATCH … END CATCH` | thân `IF`, `WHILE`, procedure |
| `CASE … END` nhiều dòng | |
| Câu lệnh nhiều dòng | `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `WITH` (CTE), `EXEC`, `DECLARE`, `CREATE`/`ALTER` |
| Cả procedure / function / view / trigger | từ `CREATE`/`ALTER` tới hết batch (trước `GO`) |
| Ngoặc `( … )` nhiều dòng | subquery, danh sách cột của `CREATE TABLE`, `VALUES` |
| Comment | `/* … */` nhiều dòng; nhiều dòng `--` liền nhau |
| Vùng tự đặt tên | từ `-- #region Tên` tới `-- #endregion` |

```sql
-- #region Chuẩn bị dữ liệu
DECLARE @from date = '2024-01-01',
        @to   date = '2024-12-31';
-- #endregion

IF @from < @to
BEGIN
    SELECT c.CustomerID,
           c.Name
    FROM dbo.Customers c
    WHERE c.CreatedAt BETWEEN @from AND @to;
END
```

**Cấp (level):** vùng không nằm trong vùng nào khác là cấp 1, vùng nằm ngay trong nó là cấp 2, … Ở ví dụ trên, `#region`
và khối `BEGIN … END` là cấp 1; `DECLARE` và `SELECT` là cấp 2.

Phím tắt (bấm **Ctrl+K**, thả ra, rồi bấm phím thứ hai), cũng có ở **Tools › SqlSense › Folding**:

| Phím | Lệnh |
|---|---|
| Ctrl+K, Ctrl+1 … Ctrl+7 | **Fold Level N**: gập mọi vùng cấp N (trừ vùng đang chứa con trỏ) |
| Ctrl+K, Ctrl+0 | **Fold All**: gập tất cả |
| Ctrl+K, Ctrl+J | **Unfold All**: mở tất cả |
| Ctrl+K, Ctrl+[ | **Fold Recursively**: gập vùng tại con trỏ và mọi vùng bên trong |
| Ctrl+K, Ctrl+] | **Unfold Recursively**: mở vùng tại con trỏ và mọi vùng bên trong |
| Ctrl+K, Ctrl+8 | **Fold All #region Blocks**: gập mọi vùng `-- #region` |
| (chỉ có trong menu) | **Fold All Comments**: gập mọi comment |
| Ctrl+M, Ctrl+M | Gập/mở vùng tại con trỏ (lệnh có sẵn của SSMS) |

Mẹo: muốn xem "dàn ý" một script dài, bấm **Ctrl+K, Ctrl+1** — mỗi câu lệnh, khối và region ngoài cùng thu về một dòng.

Tuỳ chọn ở Tools › Options › **SqlSense › General**: *Code folding* (bật/tắt cả tính năng) và *Fold statements*
(tắt nếu chỉ muốn gập khối, ngoặc, comment và region, không gập từng câu lệnh).

---

## 5. Làm việc với kết quả truy vấn

Chuột phải trên lưới kết quả (Results grid):

| Lệnh | Tác dụng |
|---|---|
| **Open Results in Excel** | Lưu lưới kết quả thành file `.xlsx` (giữ kiểu số/ngày) và mở bằng Excel. Tối đa 1.048.575 dòng (giới hạn Excel); nếu máy không có ứng dụng mở `.xlsx`, đường dẫn file hiện ở thanh trạng thái |
| **Script Results as INSERT** | Sinh các câu `INSERT … VALUES` từ kết quả, mở trong cửa sổ query mới (không mở được thì copy vào clipboard). Tên bảng đích được đoán từ câu SELECT đã chạy |

Nếu chọn nhiều ô trước khi bấm, chỉ các ô đã chọn được xuất. Dùng để: chuyển dữ liệu mẫu giữa môi trường,
tạo script seed/test data, gửi số liệu cho người không dùng SSMS.

---

## 6. Tô màu cửa sổ theo kết nối (tránh chạy nhầm PROD)

Mỗi cửa sổ query được tô màu tab, khung editor và thanh trạng thái theo server/database đang kết nối.

### 6.1 Đặt màu nhanh từ tab

Chuột phải lên **tab** cửa sổ query › **Connection Color**:

* **Db ›** chọn màu — áp dụng cho đúng *server/database* này.
* **Server ›** chọn màu — áp dụng cho mọi database trên server này.
* **No Color** — không tô (ví dụ loại `tempdb` khỏi màu của server), **Custom...** — chọn màu tuỳ ý,
  **Remove Rule** — xoá quy tắc.

### 6.2 File quy tắc

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

## 7. Thiết lập và xử lý sự cố

### 7.1 Menu Tools › SqlSense

| Lệnh | Khi nào dùng |
|---|---|
| Format SQL (Ctrl+K, Ctrl+Y) | Định dạng |
| Expand Wildcard (Ctrl+B, Ctrl+W) | Mở rộng `SELECT *` |
| Folding › … | Gập/mở code theo cấp (mục 4) |
| **Refresh Suggestions** (Ctrl+Shift+D) | Tải lại ngay metadata của database đang dùng ở cửa sổ query (hoặc database của `USE` phía trên con trỏ). Gợi ý vẫn dùng bản cũ cho tới khi bản mới xong |
| Refresh All Suggestions | Đánh dấu mọi database/linked server đã cache để tải lại ở lần dùng tiếp theo (ví dụ sau khi người khác sửa schema trên nhiều database) |
| Disable / Enable SqlSense | Tạm tắt toàn bộ SqlSense |
| **Show Log** | Xem chẩn đoán (Output pane "SqlSense") |
| Import SQL Prompt Style... | Nhập style định dạng |
| Edit Connection Colors... | Sửa quy tắc màu |

Toàn bộ tuỳ chọn: **Tools › Options › SqlSense** (General, Suggestions, Formatting, Connection Colors).

**Tự làm mới sau DDL:** khi bạn chạy (F5 / Execute) một script có `CREATE`, `ALTER`, `DROP` (bảng, view, procedure,
function, trigger, index, schema, user, role …), `SELECT … INTO` một bảng thật hoặc `sp_rename`, SqlSense tải lại metadata
của các database bị ảnh hưởng khoảng 1–2 giây sau đó. Chỉ xét phần được bôi đen nếu có; theo cả `USE` và tên ba phần
(`Archive.dbo.T`). Bảng tạm `#t`, biến bảng không tính. DDL chạy lâu (ví dụ tạo index trên bảng lớn) thì lần đọc
metadata chờ tới khi DDL xong. DDL qua SQL động (`EXEC('…')`) hoặc từ công cụ khác thì dùng Ctrl+Shift+D.
Tắt ở Tools › Options › SqlSense › General › *Refresh metadata after running CREATE/ALTER/DROP*.

**Trạng thái tải metadata:** mỗi lần SqlSense đọc danh sách object (lần đầu dùng một kết nối/database, khi nhắc tới
database khác hoặc linked server, sau Refresh Suggestions), thanh trạng thái dưới cùng của SSMS hiện biểu tượng động và
dòng *SqlSense: loading metadata for SERVER/Db…*, kể cả khi refresh (thủ công hoặc sau DDL). Xong thì hiện *metadata ready for SERVER/Db: 1.234 objects in 0,8 s*;
lỗi thì hiện lý do (chi tiết trong Show Log). Lần làm mới định kỳ (sau thời gian cache) chạy ngầm, không thông báo.
Tắt ở Tools › Options › SqlSense › General › *Show metadata loading in the status bar*.

### 7.2 Sự cố thường gặp

| Hiện tượng | Nguyên nhân / cách xử lý |
|---|---|
| Không có menu Tools › SqlSense | Cài vào sai instance hoặc SSMS mở khi đang cài. Đóng SSMS, chạy lại `Install.cmd`; kiểm tra Extensions › Manage Extensions |
| Hai danh sách gợi ý cùng hiện | Tắt IntelliSense của SSMS (mục 1.4) rồi mở lại cửa sổ query |
| Chỉ gợi ý từ khoá/hàm, không có bảng/cột | Đăng nhập bằng **Microsoft Entra ID** (chưa hỗ trợ — dùng Windows/SQL auth), SSMS không cung cấp mật khẩu SQL login (tick *Remember password*), hoặc schema đang nạp lần đầu (database lớn mất vài giây — thanh trạng thái hiện *loading metadata…*). Xem Show Log |
| Thiếu bảng/cột vừa tạo | Nếu tạo bằng script trong SSMS thì thường tự cập nhật sau vài giây; nếu không (SQL động, công cụ khác, người khác sửa) bấm **Ctrl+Shift+D** (Refresh Suggestions) |
| Đổi database ở dropdown nhưng gợi ý vẫn của database cũ | Thêm `USE TenDb` ở đầu script, hoặc Ctrl+Shift+D |
| Ctrl+Shift+D không làm gì / làm việc khác | Phím tắt bị lệnh khác chiếm: gán lại ở Tools › Options › Environment › Keyboard, lệnh *SqlSense.RefreshSuggestions* (tương tự cho phím gập code: lệnh *SqlSense.FoldLevel1*, *SqlSense.FoldAll*, …) |
| Mũi tên gập code hiện hai lần ở cùng dòng | Có thể SSMS cũng đang tự gập theo câu lệnh. Tắt IntelliSense của SSMS (mục 1.4) rồi mở lại cửa sổ query, hoặc tắt *Fold statements* của SqlSense |
| Format không làm gì | Xem thanh trạng thái: formatter từ chối vì kết quả sẽ thay đổi token (SQL lỗi cú pháp nặng thường gây ra). Gửi đoạn SQL đó cho nhóm phát triển |
| Cài báo lỗi | Xem log VSIXInstaller ở đường dẫn script in ra (`%TEMP%\SqlSense_install_*.log`) |

Khi báo lỗi, gửi kèm nội dung **Show Log**, phiên bản SqlSense (Extensions › Manage Extensions) và phiên bản SSMS
(Help › About).

---

## 8. Dành cho người build bộ cài

Yêu cầu: Visual Studio 2022/2026 với workload **Visual Studio extension development**, .NET SDK và Node.js 22+
(để sinh tài liệu Word từ `deploy\HUONG-DAN-SU-DUNG.md`; lần đầu script tự chạy `npm ci` trong `deploy\tools`).

```powershell
.\deploy\Build-Package.ps1 -Version 1.5.1           # test + rebuild + đóng gói
.\deploy\Build-Package.ps1 -Version 1.5.1 -Install  # ... và cài luôn vào SSMS trên máy này
.\deploy\Build-Package.ps1 -SkipTests               # giữ nguyên phiên bản, bỏ qua unit test
```

Kết quả: `dist\SqlSense-<version>\` và `dist\SqlSense-<version>.zip` (VSIX + script cài/gỡ + tài liệu Word +
LICENSE), cùng `dist\SqlSense-<version>.vsix` và `dist\HUONG-DAN-SU-DUNG-<version>.docx` để đính kèm riêng lên
GitHub Release; `dist\SqlSense.vsix` luôn là bản build mới nhất. **Phải tăng `-Version`** mỗi lần phát hành, nếu không máy đã cài
bản cũ sẽ từ chối cài đè.
