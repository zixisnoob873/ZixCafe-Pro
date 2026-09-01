using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ZixCafe.Infrastructure;

#nullable disable

namespace ZixCafe.Infrastructure.Migrations
{
    [DbContext(typeof(ZixCafeDbContext))]
    [Migration("20260901020000_AddOpsFeatures")]
    partial class AddOpsFeatures
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "9.0.0");
#pragma warning restore 612, 618
        }
    }
}
