---
title: SqlSense cho SSMS 22
hide:
  - navigation
---

# SqlSense cho SSMS 22

Extension miễn phí, mã nguồn mở cho **SQL Server Management Studio 22**: gợi ý T-SQL theo schema, tự sinh code
thường gặp và định dạng SQL mà không bao giờ làm đổi nghĩa câu lệnh.

[:material-download: Tải bản mới nhất](https://github.com/dathoang/SqlSense/releases/latest){ .md-button .md-button--primary }
[:material-book-open-variant: Hướng dẫn sử dụng](huong-dan.md){ .md-button }

## Tính năng

<div class="grid cards" markdown>

-   :material-lightbulb-on-outline: **Gợi ý theo ngữ cảnh**

    ---

    Bảng sau `FROM`/`JOIN`, cột theo alias, procedure sau `EXEC`, kiểu dữ liệu, database, biến. Hiểu CTE, bảng tạm,
    biến bảng. Tìm mờ: gõ `soh` ra `SalesOrderHeader`. Câu lệnh phân quyền (`ALTER ROLE`, `ADD MEMBER`, `GRANT … TO`)
    gợi ý user, role, login, schema có sẵn; `BACKUP`/`RESTORE`, `DBCC`, `ALTER DATABASE`, `ALTER INDEX` gợi ý
    database, file backup gần nhất, tuỳ chọn `WITH`, index. Rê chuột lên tên để xem cột, tham số, thành viên role.

-   :material-auto-fix: **Tự sinh code**

    ---

    Alias không trùng, điều kiện `JOIN … ON` từ khoá ngoại và cột cùng tên, khuôn `INSERT … VALUES` và tham số `EXEC`, mở rộng
    `SELECT *` thành danh sách cột, snippet (`ssf`, `bt`, `cte` … + Tab).

-   :material-file-replace-outline: **ALTER có sẵn định nghĩa**

    ---

    Gõ `ALTER PROCEDURE ` rồi chọn object: toàn bộ định nghĩa hiện tại được chèn vào, `CREATE` đổi thành `ALTER`.
    Áp dụng cho function, view, trigger.

-   :material-format-align-left: **Định dạng an toàn**

    ---

    **Ctrl+K, Ctrl+Y**. Sau khi định dạng, SqlSense so lại từng token với bản gốc. Nếu có gì khác ngoài khoảng trắng và
    hoa/thường thì không áp dụng. Nhập được style của SQL Prompt.

-   :material-unfold-less-horizontal: **Thu gọn code theo cấp**

    ---

    Thu gọn `BEGIN … END`, `CASE`, câu lệnh nhiều dòng, ngoặc, comment và khối `-- #region`. Như VS Code:
    **Ctrl+K, Ctrl+1…7** thu gọn theo cấp, **Ctrl+K, Ctrl+0** thu gọn tất cả, **Ctrl+K, Ctrl+J** mở hết.

-   :material-table-arrow-right: **Xuất kết quả**

    ---

    Chuột phải trên lưới kết quả: mở bằng Excel (giữ kiểu số/ngày) hoặc sinh script `INSERT` từ dữ liệu.

-   :material-palette-outline: **Màu theo kết nối**

    ---

    Tab, khung editor và thanh trạng thái được tô màu theo server/database, ví dụ PROD màu đỏ, để tránh chạy nhầm
    môi trường.

</div>

## Cài đặt nhanh

1. Tải `SqlSense-<phiên bản>.zip` ở trang [Releases](https://github.com/dathoang/SqlSense/releases/latest) và giải nén.
2. Đóng SSMS 22.
3. Nhấp đúp `Install.cmd`. Không cần quyền Administrator.

Sau khi cài, menu **Tools › SqlSense** xuất hiện trong SSMS. Hướng dẫn chi tiết, cách thiết lập và xử lý sự cố có ở trang
[Hướng dẫn sử dụng](huong-dan.md); bản Word nằm trong file zip và cũng được đính kèm riêng ở trang Releases.

!!! info "Yêu cầu"
    Windows 10/11 x64 và SQL Server Management Studio 22 (x64). Gợi ý theo schema cần đăng nhập bằng Windows hoặc
    SQL Server Authentication; Microsoft Entra ID hiện chỉ có gợi ý từ khoá, hàm và snippet.

## Mã nguồn

SqlSense là phần mềm mã nguồn mở theo [giấy phép MIT](https://github.com/dathoang/SqlSense/blob/main/LICENSE).
Mã nguồn, cách build và báo lỗi: [github.com/dathoang/SqlSense](https://github.com/dathoang/SqlSense).
