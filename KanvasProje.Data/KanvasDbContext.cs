using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using KanvasProje.Core.Models;
using KanvasProje.Core.Varliklar;

namespace KanvasProje.Data
{
    public class KanvasDbContext : IdentityDbContext<AppUser>
    {
        public KanvasDbContext(DbContextOptions<KanvasDbContext> options) : base(options)
        {
        }

        public DbSet<SiteAyarlari> SiteAyarlari { get; set; }
        public DbSet<Slayt> Slaytlar { get; set; }
        public DbSet<Kategori> Kategoriler { get; set; }
        public DbSet<Urun> Urunler { get; set; }
        public DbSet<UrunSecenek> UrunSecenekleri { get; set; }
        public DbSet<UrunResim> UrunResimleri { get; set; }
        public DbSet<UrunOzellikTanimi> UrunOzellikTanimlari { get; set; }
        public DbSet<UrunOzellikDegeri> UrunOzellikDegerleri { get; set; }
        public DbSet<Siparis> Siparisler { get; set; }
        public DbSet<SiparisDetay> SiparisDetaylari { get; set; }
        public DbSet<Sepet> Sepetler { get; set; }
        public DbSet<SepetItem> SepetItems { get; set; }
        public DbSet<Adres> Adresler { get; set; }
        public DbSet<Favori> Favoriler { get; set; }
        public DbSet<Yorum> Yorumlar { get; set; }
        public DbSet<IletisimMesaj> IletisimMesajlari { get; set; }
        public DbSet<BultenAboneligi> BultenAbonelikleri { get; set; }
        public DbSet<Kupon> Kuponlar { get; set; }
        public DbSet<IadeTalebi> IadeTalepleri { get; set; }
        public DbSet<KargoFirmasi> KargoFirmalari { get; set; }
        public DbSet<KurumsalSayfa> KurumsalSayfalar { get; set; }
        public DbSet<ZiyaretciLog> ZiyaretciLoglari { get; set; }
        public DbSet<SenTasarla> SenTasarla { get; set; }
        public DbSet<HomePageSection> HomePageSections { get; set; }
        public DbSet<HomePageSectionProduct> HomePageSectionProducts { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            optionsBuilder.ConfigureWarnings(warnings =>
            {
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning);
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning);
            });
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SiteAyarlari>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<Slayt>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<Kategori>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Slug).HasFilter("\"Slug\" IS NOT NULL AND \"Slug\" <> ''");
                entity.HasOne(e => e.ParentKategori)
                    .WithMany(e => e.AltKategoriler)
                    .HasForeignKey(e => e.ParentKategoriId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Urun>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.KategoriId);
                entity.HasIndex(e => e.Slug).HasFilter("\"Slug\" IS NOT NULL AND \"Slug\" <> ''");
                entity.HasOne(e => e.Kategori)
                    .WithMany(e => e.Urunler)
                    .HasForeignKey(e => e.KategoriId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UrunSecenek>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.UrunId);
                entity.HasOne(e => e.Urun)
                    .WithMany(e => e.UrunSecenek)
                    .HasForeignKey(e => e.UrunId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UrunResim>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UrunId, e.Sira });
                entity.HasOne(e => e.Urun)
                    .WithMany(e => e.UrunResimleri)
                    .HasForeignKey(e => e.UrunId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UrunOzellikTanimi>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UrunTipi, e.Kod }).IsUnique();
            });

            modelBuilder.Entity<UrunOzellikDegeri>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.UrunId);
                entity.HasIndex(e => e.UrunOzellikTanimiId);
                entity.HasOne(e => e.Urun)
                    .WithMany(e => e.UrunOzellikleri)
                    .HasForeignKey(e => e.UrunId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.UrunOzellikTanimi)
                    .WithMany(e => e.UrunOzellikDegerleri)
                    .HasForeignKey(e => e.UrunOzellikTanimiId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Siparis>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.AppUserId);
                entity.HasOne(e => e.AppUser)
                    .WithMany()
                    .HasForeignKey(e => e.AppUserId);
            });

            modelBuilder.Entity<SiparisDetay>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.SiparisId);
                entity.HasIndex(e => e.UrunId);
                entity.HasIndex(e => e.UrunSecenekId);
                entity.HasOne(e => e.Siparis)
                    .WithMany(e => e.SiparisDetaylari)
                    .HasForeignKey(e => e.SiparisId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Urun)
                    .WithMany()
                    .HasForeignKey(e => e.UrunId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.UrunSecenek)
                    .WithMany()
                    .HasForeignKey(e => e.UrunSecenekId);
            });

            modelBuilder.Entity<Sepet>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.AppUserId);
                entity.HasOne(e => e.AppUser)
                    .WithMany()
                    .HasForeignKey(e => e.AppUserId);
            });

            modelBuilder.Entity<SepetItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.SepetId);
                entity.HasIndex(e => e.UrunId);
                entity.HasIndex(e => e.UrunSecenekId);
                entity.HasOne(e => e.Sepet)
                    .WithMany(e => e.SepetItems)
                    .HasForeignKey(e => e.SepetId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Urun)
                    .WithMany()
                    .HasForeignKey(e => e.UrunId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.UrunSecenek)
                    .WithMany()
                    .HasForeignKey(e => e.UrunSecenekId);
            });

            modelBuilder.Entity<Adres>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.AppUser)
                    .WithMany()
                    .HasForeignKey(e => e.AppUserId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
            });

            modelBuilder.Entity<Favori>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.AppUserId);
                entity.HasIndex(e => e.UrunId);
                entity.HasOne(e => e.AppUser)
                    .WithMany()
                    .HasForeignKey(e => e.AppUserId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
                entity.HasOne(e => e.Urun)
                    .WithMany()
                    .HasForeignKey(e => e.UrunId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
            });

            modelBuilder.Entity<Yorum>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.UrunId);
                entity.HasOne(e => e.Urun)
                    .WithMany()
                    .HasForeignKey(e => e.UrunId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
            });

            modelBuilder.Entity<IletisimMesaj>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<BultenAboneligi>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Email).IsUnique();
            });

            modelBuilder.Entity<Kupon>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<IadeTalebi>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.SiparisId);
                entity.HasOne(e => e.Siparis)
                    .WithMany()
                    .HasForeignKey(e => e.SiparisId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
            });

            modelBuilder.Entity<KargoFirmasi>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Kod).IsUnique();
            });

            modelBuilder.Entity<KurumsalSayfa>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<ZiyaretciLog>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<SenTasarla>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<HomePageSection>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<HomePageSectionProduct>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.SectionId);
                entity.HasIndex(e => e.UrunId);
                entity.HasOne(e => e.Section)
                    .WithMany(e => e.SectionProducts)
                    .HasForeignKey(e => e.SectionId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Urun)
                    .WithMany()
                    .HasForeignKey(e => e.UrunId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

        }
    }
}