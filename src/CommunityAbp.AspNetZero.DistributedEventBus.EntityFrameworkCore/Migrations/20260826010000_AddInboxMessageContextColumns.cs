using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.Migrations;

public partial class AddInboxMessageContextColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "DispatchMode",
            table: "InboxMessages",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<long>(
            name: "DeliveryCount",
            table: "InboxMessages",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EntityPath",
            table: "InboxMessages",
            type: "nvarchar(260)",
            maxLength: 260,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LegacyTypeIdentifier",
            table: "InboxMessages",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SubscriptionName",
            table: "InboxMessages",
            type: "nvarchar(260)",
            maxLength: 260,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DispatchMode", table: "InboxMessages");
        migrationBuilder.DropColumn(name: "DeliveryCount", table: "InboxMessages");
        migrationBuilder.DropColumn(name: "EntityPath", table: "InboxMessages");
        migrationBuilder.DropColumn(name: "LegacyTypeIdentifier", table: "InboxMessages");
        migrationBuilder.DropColumn(name: "SubscriptionName", table: "InboxMessages");
    }
}
