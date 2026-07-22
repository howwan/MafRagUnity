
Export i18n support is now complete. Here's a summary of what was done:
  **Backend changes:**

  - Created ExportI18n.java — translation maps for PDF, lead, and log export

  content in both zh-CN and en

  - Updated AnalyticsService.exportPdfReport() to accept locale parameter and

  use ExportI18n for all PDF labels

  - Updated LeadService.exportExcel() to use localized headers and status text

  - Updated LogService.exportExcel() to use localized headers

  - Updated all three controllers to accept lang query parameter

  - Fixed PDF Chinese font loading: uses TrueTypeCollection +

  PDType0Font.load(doc, ttf, true) to embed fonts from .ttc files (STHeiti,

  PingFang, etc.)

  - Added fallback: if no Chinese font found, auto-switches to English output

  **Frontend changes:**

  - Updated downloadBlob() utility to pass lang as query parameter

  - Updated dashboard.ts, leads.ts, logs.ts API modules to read current locale

  via i18n.global.locale.value and pass it to export endpoints

  **Verification:**

  - All 6 export combinations (3 endpoints × 2 languages) return HTTP 200

  - PDF ZH: 28KB (embedded Chinese font) vs EN: 9KB (Helvetica)

  - Leads ZH: 6.9KB (Chinese headers) vs EN: 6.8KB (English headers)

  - Logs ZH: 19.8KB vs EN: 19.8KB

---

 All tasks are complete. Here's a summary of everything implemented:

  

  **What was done**


  **Backend (****./backend****)**


  1. **Added Apache PDFBox 3.0.1** dependency to build.gradle

  2. **Created** **FileExportUtil.java** — shared utility for Excel (SXSSFWorkbook

  streaming) and PDF response helpers

  3. **GET /api/analytics/export/pdf** — generates PDF with core metrics, trend data

   table, device/browser distribution

  4. **GET /api/leads/export** — streams Excel with all lead fields + trajectory

  summary, paginated queries (1000/batch)

  5. **GET /api/logs/export** — streams Excel with all audit log fields, paginated

  queries (1000/batch)

  6. **@Log** **annotations** on all export endpoints for audit logging

  7. **Auth preserved** — same @PreAuthorize as existing list endpoints

  

  **Frontend (****./admin****)**

  

  8. **Created** **src/utils/download.ts** — shared Blob download utility using fetch()

  to handle binary responses

  9. **Updated dashboard export** — button calls server PDF endpoint instead of

  client-side html2canvas/jsPDF

  10. **Updated leads/logs export** — API modules now call server Excel endpoints

  instead of client-side xlsx

  11. Remove unused html2canvas/jspdf imports from dashboard view

  

  **Integration Tests**

  

  12. **Java IT**: ExportIntegrationTest.java — 6 tests (PDF/Excel with/without

  filters)

  13. **Shell IT**: test_export.sh — 9 tests across PDF, Excel, auth checks

  14. **Playwright E2E**: export.spec.ts — 4 tests (PDF, leads Excel, logs Excel,

  post-export state)

  15. **Registered** export test in integration_test.sh

  16. **Verified** all shell ITs pass, all Playwright tests pass, existing tests

  unaffected

  

  **Planning Documents**

  

  Created spec-extra/export-enhancement/ with requirements.md, design.md,

  tasks.md