/* ============================================================
   CANVASIA ADMIN — JavaScript Module
   ============================================================ */

document.addEventListener('DOMContentLoaded', function () {

    // --- Elements ---
    const sidebar = document.getElementById('caSidebar');
    const toggleBtn = document.getElementById('caSidebarToggle');
    const overlay = document.getElementById('caOverlay');
    const themeToggle = document.getElementById('caThemeToggle');
    const mobileMenuBtn = document.getElementById('caMobileMenuBtn');
    const mobileMenuIcon = document.getElementById('caMobileMenuIcon');
    const mobileNavPanel = document.getElementById('caMobileNavPanel');
    const mobileNavBackdrop = document.getElementById('caMobileNavBackdrop');

    // =============================================
    // MOBILE TOP-DOWN MENU TOGGLE
    // =============================================
    function closeMobileMenu() {
        if (!mobileNavPanel || !mobileMenuBtn) return;
        mobileNavPanel.classList.remove('open');
        if (mobileNavBackdrop) mobileNavBackdrop.classList.remove('open');
        mobileMenuBtn.classList.remove('active');
        mobileMenuBtn.setAttribute('aria-expanded', 'false');
        if (mobileMenuIcon) {
            mobileMenuIcon.className = 'fas fa-bars';
        }
        document.body.style.overflow = '';
    }

    function openMobileMenu() {
        if (!mobileNavPanel || !mobileMenuBtn) return;
        mobileNavPanel.classList.add('open');
        if (mobileNavBackdrop) mobileNavBackdrop.classList.add('open');
        mobileMenuBtn.classList.add('active');
        mobileMenuBtn.setAttribute('aria-expanded', 'true');
        if (mobileMenuIcon) {
            mobileMenuIcon.className = 'fas fa-xmark';
        }
        document.body.style.overflow = 'hidden';
    }

    if (mobileMenuBtn && mobileNavPanel) {
        mobileMenuBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            if (mobileNavPanel.classList.contains('open')) {
                closeMobileMenu();
            } else {
                openMobileMenu();
            }
        });

        if (mobileNavBackdrop) {
            mobileNavBackdrop.addEventListener('click', closeMobileMenu);
        }

        // Accordion toggles inside mobile menu
        mobileNavPanel.querySelectorAll('.ca-mobile-group-toggle').forEach(function (btn) {
            btn.addEventListener('click', function () {
                const group = btn.closest('.ca-mobile-nav-group');
                if (!group) return;
                const isOpen = group.classList.contains('open');
                // Close other open groups for a clean accordion feel
                mobileNavPanel.querySelectorAll('.ca-mobile-nav-group.open').forEach(function (other) {
                    if (other !== group) {
                        other.classList.remove('open');
                        const otherBtn = other.querySelector('.ca-mobile-group-toggle');
                        if (otherBtn) otherBtn.setAttribute('aria-expanded', 'false');
                    }
                });
                group.classList.toggle('open', !isOpen);
                btn.setAttribute('aria-expanded', !isOpen ? 'true' : 'false');
            });
        });

        // Close on Escape key or desktop resize
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && mobileNavPanel.classList.contains('open')) {
                closeMobileMenu();
            }
        });

        window.addEventListener('resize', function () {
            if (window.innerWidth > 1080 && mobileNavPanel.classList.contains('open')) {
                closeMobileMenu();
            }
        });
    }

    // =============================================
    // SIDEBAR TOGGLE
    // =============================================
    if (toggleBtn && sidebar) {
        toggleBtn.addEventListener('click', function () {
            if (window.innerWidth <= 1024) {
                sidebar.classList.toggle('mobile-open');
                if (overlay) overlay.classList.toggle('active');
            } else {
                sidebar.classList.toggle('collapsed');
                localStorage.setItem('ca-sidebar', sidebar.classList.contains('collapsed') ? 'collapsed' : 'open');
            }
        });

        // Overlay click closes sidebar on mobile
        if (overlay) {
            overlay.addEventListener('click', function () {
                sidebar.classList.remove('mobile-open');
                overlay.classList.remove('active');
            });
        }

        // Restore sidebar state from localStorage
        if (window.innerWidth > 1024) {
            var saved = localStorage.getItem('ca-sidebar');
            if (saved === 'collapsed') {
                sidebar.classList.add('collapsed');
            }
        }
    }

    // =============================================
    // LIGHT-ONLY ADMIN EXPERIENCE
    // =============================================
    document.documentElement.removeAttribute('data-theme');
    localStorage.removeItem('ca-theme');

    if (themeToggle) {
        updateThemeIcon('light');
        themeToggle.addEventListener('click', function (event) {
            event.preventDefault();
            document.documentElement.removeAttribute('data-theme');
            localStorage.removeItem('ca-theme');
            updateThemeIcon('light');
        });
    }

    function updateThemeIcon(theme) {
        if (!themeToggle) return;
        var icon = themeToggle.querySelector('i');
        if (icon) {
            icon.className = theme === 'dark' ? 'fas fa-sun' : 'fas fa-moon';
        }
    }

    // =============================================
    // AUTO-DISMISS SUCCESS ALERTS
    // =============================================
    document.querySelectorAll('.ca-alert-success[data-auto-dismiss]').forEach(function (alert) {
        setTimeout(function () {
            alert.style.opacity = '0';
            alert.style.transform = 'translateY(-10px)';
            setTimeout(function () { alert.remove(); }, 400);
        }, 4000);
    });

    // =============================================
    // CONTENT FADE-IN
    // =============================================
    var content = document.querySelector('.ca-content');
    if (content) {
        content.classList.add('ca-fade-in');
    }

    // =============================================
    // TOOLTIPS (Bootstrap)
    // =============================================
    var tooltips = document.querySelectorAll('[data-bs-toggle="tooltip"]');
    tooltips.forEach(function (el) {
        new bootstrap.Tooltip(el);
    });

}); // end DOMContentLoaded


// =============================================
// GLOBAL: TOAST NOTIFICATION
// =============================================
function showToast(message, type) {
    type = type || 'info';
    var container = document.getElementById('caToastContainer');
    if (!container) {
        container = document.createElement('div');
        container.id = 'caToastContainer';
        container.className = 'ca-toast-container';
        document.body.appendChild(container);
    }

    var iconMap = {
        success: 'fas fa-check-circle',
        error: 'fas fa-exclamation-circle',
        warning: 'fas fa-exclamation-triangle',
        info: 'fas fa-info-circle'
    };

    var toast = document.createElement('div');
    toast.className = 'ca-toast ' + type;
    toast.innerHTML = '<i class="' + (iconMap[type] || iconMap.info) + '"></i><span>' + message + '</span>';
    container.appendChild(toast);

    setTimeout(function () {
        toast.style.opacity = '0';
        toast.style.transform = 'translateX(40px)';
        toast.style.transition = 'all .3s ease';
        setTimeout(function () { toast.remove(); }, 300);
    }, 3500);
}


// =============================================
// GLOBAL: IMAGE PREVIEW (for upload forms)
// =============================================
function previewImage(event) {
    var reader = new FileReader();
    reader.onload = function () {
        var output = document.getElementById('imagePreview');
        var content = document.getElementById('uploadContent');
        if (output) {
            output.src = reader.result;
            output.style.display = 'block';
        }
        if (content) {
            content.style.display = 'none';
        }
    };
    if (event.target.files && event.target.files[0]) {
        reader.readAsDataURL(event.target.files[0]);
    }
}


// =============================================
// GLOBAL: CONFIRM DELETE (SweetAlert fallback)
// =============================================
function confirmDelete(url, itemName) {
    if (typeof Swal !== 'undefined') {
        Swal.fire({
            title: 'Emin misiniz?',
            text: (itemName || 'Bu öğe') + ' silinecektir. Bu işlem geri alınamaz.',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonColor: '#ef4444',
            cancelButtonColor: '#64748b',
            confirmButtonText: 'Evet, Sil',
            cancelButtonText: 'İptal'
        }).then(function (result) {
            if (result.isConfirmed) {
                window.location.href = url;
            }
        });
    } else {
        if (confirm('Bu öğeyi silmek istediğinize emin misiniz?')) {
            window.location.href = url;
        }
    }
    return false;
}
