using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class BackfillProductionTransferWorkflowStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE pt
                   SET pt.WorkflowStatus = CASE
                       WHEN wt.Status = 11 THEN 'Cancelled'
                       WHEN wt.Status = 14 THEN 'CompletedWithShortage'
                       WHEN wt.Status = 10 THEN 'Completed'
                       WHEN wt.Status = 13 THEN 'AwaitingHandover'
                       WHEN wt.Status IN (3, 4, 5, 6, 7, 8, 9, 12) THEN 'Picking'
                       ELSE 'Planned'
                   END
                FROM RII_PT_HEADER_LINK AS pt
                INNER JOIN RII_WT_HEADER AS wt ON wt.Id = pt.WarehouseTransferHeaderId;

                UPDATE ptl
                   SET ptl.HandedOverQuantity = CASE
                           WHEN wt.Status IN (10, 14) THEN wtl.PutawayQuantity
                           ELSE 0
                       END,
                       ptl.ShortClosedQuantity = CASE
                           WHEN wt.Status = 14 THEN wtl.ShortClosedQuantity
                           ELSE 0
                       END
                FROM RII_PT_LINE_LINK AS ptl
                INNER JOIN RII_WT_LINE AS wtl ON wtl.Id = ptl.WarehouseTransferLineId
                INNER JOIN RII_WT_HEADER AS wt ON wt.Id = wtl.WtHeaderId;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
