# =============================================================
# FlashStore - Terraform: infraestructura en Google Cloud
#
# Crea:
#   - VPC propia y subred (no usa la red "default" del proyecto)
#   - Firewall: solo 22, 3000 y 5000, y solo desde allowed_cidrs
#   - IP pública estática
#   - VM Ubuntu 24.04 LTS (e2-medium) con la llave SSH del equipo
#   - Inventario de Ansible generado con la IP real
#
# Lo que NO se expone: PostgreSQL (5433), Redis (6380),
# RabbitMQ (5672/15672). Se administran por SSH / CLI.
# =============================================================

locals {
  name_prefix = "flashstore-${var.environment}"

  labels = {
    project     = "flashstore"
    environment = var.environment
    managed_by  = "terraform"
    course      = "umg-048-qa"
  }

  ssh_public_key = trimspace(file(pathexpand(var.ssh_public_key_path)))
}

# ---------- Red ----------
resource "google_compute_network" "vpc" {
  name                    = "${local.name_prefix}-vpc"
  auto_create_subnetworks = false
  description             = "Red de FlashStore (${var.environment})"
}

resource "google_compute_subnetwork" "subnet" {
  name          = "${local.name_prefix}-subnet"
  ip_cidr_range = "10.10.0.0/24"
  region        = var.region
  network       = google_compute_network.vpc.id
}

# ---------- Firewall ----------
resource "google_compute_firewall" "ssh" {
  name        = "${local.name_prefix}-allow-ssh"
  network     = google_compute_network.vpc.name
  description = "SSH solo desde las redes del equipo (Ansible, CLI y Jenkins)"
  direction   = "INGRESS"
  priority    = 1000

  allow {
    protocol = "tcp"
    ports    = ["22"]
  }

  source_ranges = var.allowed_cidrs
  target_tags   = ["flashstore-app"]
}

resource "google_compute_firewall" "app" {
  name        = "${local.name_prefix}-allow-app"
  network     = google_compute_network.vpc.name
  description = "PWA (3000) y API (5000) solo desde las redes del equipo"
  direction   = "INGRESS"
  priority    = 1000

  allow {
    protocol = "tcp"
    ports    = ["3000", "5000"]
  }

  source_ranges = var.allowed_cidrs
  target_tags   = ["flashstore-app"]
}

# ---------- IP pública ----------
resource "google_compute_address" "public_ip" {
  name         = "${local.name_prefix}-ip"
  region       = var.region
  address_type = "EXTERNAL"
  network_tier = "STANDARD"
}

# ---------- VM ----------
resource "google_compute_instance" "app" {
  name         = "${local.name_prefix}-vm"
  machine_type = var.machine_type
  zone         = var.zone
  tags         = ["flashstore-app"]
  labels       = local.labels

  allow_stopping_for_update = true
  deletion_protection       = false

  boot_disk {
    auto_delete = true

    initialize_params {
      image  = "ubuntu-os-cloud/ubuntu-2404-lts-amd64"
      size   = var.disk_size_gb
      type   = "pd-balanced"
      labels = local.labels
    }
  }

  network_interface {
    subnetwork = google_compute_subnetwork.subnet.id

    access_config {
      nat_ip       = google_compute_address.public_ip.address
      network_tier = "STANDARD"
    }
  }

  metadata = {
    # Se usa la llave del equipo en lugar de OS Login para que Ansible y Jenkins
    # entren con el mismo usuario y la misma llave.
    enable-oslogin         = "FALSE"
    block-project-ssh-keys = "TRUE"
    ssh-keys               = "${var.ssh_user}:${local.ssh_public_key}"
  }

  shielded_instance_config {
    enable_secure_boot          = true
    enable_vtpm                 = true
    enable_integrity_monitoring = true
  }

  scheduling {
    automatic_restart   = true
    on_host_maintenance = "MIGRATE"
    preemptible         = false
  }
}

# ---------- Inventario de Ansible ----------
resource "local_file" "ansible_inventory" {
  filename        = "${path.module}/../../ansible/inventory/gcp.ini"
  file_permission = "0644"

  content = templatefile("${path.module}/templates/inventory.tftpl", {
    host             = google_compute_address.public_ip.address
    ssh_user         = var.ssh_user
    private_key_path = var.ssh_private_key_path
    environment      = var.environment
  })
}
