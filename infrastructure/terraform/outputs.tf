output "public_ip" {
  description = "IP pública de la VM de FlashStore"
  value       = google_compute_address.public_ip.address
}

output "pwa_url" {
  description = "PWA (web, desktop y móvil)"
  value       = "http://${google_compute_address.public_ip.address}:3000"
}

output "api_url" {
  description = "API REST (Swagger en /swagger, salud en /health)"
  value       = "http://${google_compute_address.public_ip.address}:5000"
}

output "ssh_command" {
  description = "Comando para entrar a la VM"
  value       = "ssh -i ${var.ssh_private_key_path} ${var.ssh_user}@${google_compute_address.public_ip.address}"
}

output "cli_example" {
  description = "Ejemplo del CLI de FlashStore apuntando a la nube"
  value       = "flashstore health --api http://${google_compute_address.public_ip.address}:5000 --ssh ${var.ssh_user}@${google_compute_address.public_ip.address}:22"
}

output "ansible_inventory" {
  description = "Inventario generado para Ansible"
  value       = abspath(local_file.ansible_inventory.filename)
}
