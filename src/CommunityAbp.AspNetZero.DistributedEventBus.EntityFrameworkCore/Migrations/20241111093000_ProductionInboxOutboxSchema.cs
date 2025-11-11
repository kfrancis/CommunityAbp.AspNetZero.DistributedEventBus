using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.Migrations
{
 public partial class ProductionInboxOutboxSchema : Migration
 {
 protected override void Up(MigrationBuilder migrationBuilder)
 {
 migrationBuilder.CreateTable(
 name: "OutboxMessages",
 columns: table => new
 {
 Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
 EventName = table.Column<string>(type: "nvarchar(200)", maxLength:200, nullable: false),
 EventType = table.Column<string>(type: "nvarchar(200)", maxLength:200, nullable: false),
 EventData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
 CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
 SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
 Status = table.Column<string>(type: "nvarchar(40)", maxLength:40, nullable: false),
 CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength:100, nullable: true),
 Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
 RetryCount = table.Column<int>(type: "int", nullable: false),
 RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
 },
 constraints: table =>
 {
 table.PrimaryKey("PK_OutboxMessages", x => x.Id);
 });

 migrationBuilder.CreateTable(
 name: "InboxMessages",
 columns: table => new
 {
 Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
 MessageId = table.Column<string>(type: "nvarchar(max)", nullable: false),
 EventName = table.Column<string>(type: "nvarchar(200)", maxLength:200, nullable: false),
 EventType = table.Column<string>(type: "nvarchar(200)", maxLength:200, nullable: false),
 EventData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
 ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
 ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
 Status = table.Column<string>(type: "nvarchar(40)", maxLength:40, nullable: false),
 CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength:100, nullable: true),
 Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
 RetryCount = table.Column<int>(type: "int", nullable: false),
 RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
 },
 constraints: table =>
 {
 table.PrimaryKey("PK_InboxMessages", x => x.Id);
 });

 migrationBuilder.CreateIndex(
 name: "IX_InboxMessages_MessageId",
 table: "InboxMessages",
 column: "MessageId",
 unique: true);

 migrationBuilder.CreateIndex(
 name: "IX_InboxMessages_CorrelationId",
 table: "InboxMessages",
 column: "CorrelationId");

 migrationBuilder.CreateIndex(
 name: "IX_InboxMessages_Status_ReceivedAt",
 table: "InboxMessages",
 columns: new[] { "Status", "ReceivedAt" });

 migrationBuilder.CreateIndex(
 name: "IX_OutboxMessages_CorrelationId",
 table: "OutboxMessages",
 column: "CorrelationId");

 migrationBuilder.CreateIndex(
 name: "IX_OutboxMessages_Status_CreatedAt",
 table: "OutboxMessages",
 columns: new[] { "Status", "CreatedAt" });
 }

 protected override void Down(MigrationBuilder migrationBuilder)
 {
 migrationBuilder.DropTable(
 name: "InboxMessages");

 migrationBuilder.DropTable(
 name: "OutboxMessages");
 }
 }
}
