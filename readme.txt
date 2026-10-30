SOFTWARE ENGINEERING - LAB GROUP ASSIGNMENT 1
Smart Ticketing System - Ticket Vendor Machine (TVM)
Semester 1, 2026-2027 - Instructor: P.T.K. Trung

GROUP MEMBERS:
  524K0028 - Nguyễn Lê Toàn Thắng (leader)
  524V0018 - Võ Hữu Phước
  523V0007 - Hà Văn Thành Đạt

GITHUB REPOSITORY
  https://github.com/claudeprogrammorpheus-png/TicketVendor   (shared group repository)
  Members' personal repositories:
    Võ Hữu Phước      : https://github.com/PhuocTDTU/Ticket-vendor-3
    Hà Văn Thành Đạt  : https://github.com/c2kodo11/TicketVendor2

OTHER LINKS
  UI mockups      : 03_UI/tvm_mockup.html (open in a browser; screens #welcome #destination #ticket
                    #payment #qr #card #success #error) - PNG exports in 03_UI/
  Figma (optional): <link if the group re-creates the mockups in Figma>

REPOSITORY CONTENT
  01_Requirements/   Clarification_QA.xlsx, Requirements_Specification.xlsx
  02_UML/            Use case, activity (x2), sequence, state chart, class diagrams (.puml + .png)
  03_UI/             Wireframes / mockups
  04_Architecture/   MVC architecture and deployment diagrams (.puml + .png)
  05_Source/         TVM.sln - TVM.Demo (C# WinForms, .NET 9) + TVM.Tests (xUnit)
  05_Source/Database/TVM_DB.sql   MS SQL Server script (schema + seed data)
  06_Screenshots/    Screenshots of the running demo
  524K0028_NguyenLeToanThang.docx  Full report

DATABASE SCRIPT INSTRUCTIONS
  1. Requires SQL Server 2016+ or SQL Server Express LocalDB (installed with Visual Studio).
  2. Run:   sqlcmd -S "(localdb)\MSSQLLocalDB" -i 05_Source\Database\TVM_DB.sql
     or open TVM_DB.sql in SSMS / Visual Studio and Execute.
     The script drops and recreates database TVM_DB (18 stations, 612 fares, 40 sample sales).
  3. For another server, edit "ConnectionString" in 05_Source/TVM.Demo/appsettings.json.

RUN THE DEMO
  1. Open 05_Source/TVM.sln in Visual Studio 2022 (needs the .NET 9 SDK), set TVM.Demo as
     startup project, press F5.   (Command line: dotnet run --project 05_Source/TVM.Demo)
  2. Test cards:  4242 4242 4242 4242 -> approved,  4000 0000 0000 0002 -> declined.
  3. QR / wallet: press "Simulate: passenger paid in app".
  4. Refund flow: tick "Simulate printer jam on next ticket" on the welcome screen.
  5. Output report: "Sales report (admin)" button on the welcome screen.
  6. Automated walkthrough + screenshots:  TVM.Demo.exe --screenshots <folder>
  7. Unit tests:  dotnet test 05_Source/TVM.sln
  Note: if the build fails with "Could not copy ... because it was not found", the folder path is
  too long for Windows (260 characters) - move the repository to a short path such as C:\TVM.

PLANTUML DIAGRAMS
  Re-render after editing:  java -jar plantuml.jar -tpng 02_UML/*.puml 04_Architecture/*.puml
  (or use the PlantUML extension in VS Code / https://www.plantuml.com/plantuml)

CONTRIBUTION OF EACH MEMBER
  Student ID   Full name                 Main tasks                                      Contribution
  524K0028     Nguyễn Lê Toàn Thắng      Leader - requirements (Q&A, SRS), report        33.33%
  524V0018     Võ Hữu Phước              UML modelling, UI/UX wireframes, architecture   33.33%
  523V0007     Hà Văn Thành Đạt          C# / MSSQL demo, testing, GitHub                33.33%
                                                                         Total:  100%
