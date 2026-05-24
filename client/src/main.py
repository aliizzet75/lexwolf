import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import os
import json
import hashlib
from datetime import datetime
import threading
import queue

# Import conversation recorder
from conversation_recorder import ConversationRecorder

class LexWolfClient:
    def __init__(self, root):
        self.root = root
        self.root.title("LexWolf - RechtsKI für Anwälte")
        self.root.geometry("1200x800")
        
        # Application state
        self.current_case = None
        self.style_profile = None
        self.documents = []
        self.conversations = []
        
        # Conversation recorder
        self.conversation_recorder = ConversationRecorder()
        
        # Create UI
        self.create_menu()
        self.create_notebook()
        self.create_status_bar()
        
        # Load configuration
        self.load_config()
        
    def create_menu(self):
        menubar = tk.Menu(self.root)
        self.root.config(menu=menubar)
        
        # File menu
        file_menu = tk.Menu(menubar, tearoff=0)
        menubar.add_cascade(label="Datei", menu=file_menu)
        file_menu.add_command(label="Neuer Fall", command=self.new_case)
        file_menu.add_command(label="Fall öffnen", command=self.open_case)
        file_menu.add_command(label="Fall speichern", command=self.save_case)
        file_menu.add_separator()
        file_menu.add_command(label="Beenden", command=self.root.quit)
        
        # Tools menu
        tools_menu = tk.Menu(menubar, tearoff=0)
        menubar.add_cascade(label="Tools", menu=tools_menu)
        tools_menu.add_command(label="Stilanalyse", command=self.analyze_style)
        tools_menu.add_command(label="Dokumentenerstellung", command=self.create_document)
        tools_menu.add_command(label="Gespräch aufzeichnen", command=self.record_conversation)
        
        # Settings menu
        settings_menu = tk.Menu(menubar, tearoff=0)
        menubar.add_cascade(label="Einstellungen", menu=settings_menu)
        settings_menu.add_command(label="Server-Einstellungen", command=self.server_settings)
        settings_menu.add_command(label="Stilprofile", command=self.style_profiles)
        
    def create_notebook(self):
        self.notebook = ttk.Notebook(self.root)
        self.notebook.pack(fill=tk.BOTH, expand=True, padx=10, pady=10)
        
        # Cases tab
        self.cases_frame = ttk.Frame(self.notebook)
        self.notebook.add(self.cases_frame, text="Fälle")
        self.create_cases_tab()
        
        # Documents tab
        self.documents_frame = ttk.Frame(self.notebook)
        self.notebook.add(self.documents_frame, text="Dokumente")
        self.create_documents_tab()
        
        # Search tab
        self.search_frame = ttk.Frame(self.notebook)
        self.notebook.add(self.search_frame, text="Recherche")
        self.create_search_tab()
        
        # Conversations tab
        self.conversations_frame = ttk.Frame(self.notebook)
        self.notebook.add(self.conversations_frame, text="Gespräche")
        self.create_conversations_tab()
        
    def create_cases_tab(self):
        # Cases list
        cases_label = ttk.Label(self.cases_frame, text="Aktuelle Fälle", font=("Arial", 12, "bold"))
        cases_label.pack(anchor=tk.W, pady=(0, 10))
        
        # Cases treeview
        columns = ("name", "client", "date", "status")
        self.cases_tree = ttk.Treeview(self.cases_frame, columns=columns, show="headings")
        
        self.cases_tree.heading("name", text="Fallname")
        self.cases_tree.heading("client", text="Mandant")
        self.cases_tree.heading("date", text="Erstellt")
        self.cases_tree.heading("status", text="Status")
        
        self.cases_tree.pack(fill=tk.BOTH, expand=True)
        
        # Buttons
        buttons_frame = ttk.Frame(self.cases_frame)
        buttons_frame.pack(fill=tk.X, pady=(10, 0))
        
        new_case_btn = ttk.Button(buttons_frame, text="Neuer Fall", command=self.new_case)
        new_case_btn.pack(side=tk.LEFT, padx=(0, 5))
        
        open_case_btn = ttk.Button(buttons_frame, text="Fall öffnen", command=self.open_case)
        open_case_btn.pack(side=tk.LEFT, padx=(0, 5))
        
    def create_documents_tab(self):
        # Documents list
        docs_label = ttk.Label(self.documents_frame, text="Dokumente", font=("Arial", 12, "bold"))
        docs_label.pack(anchor=tk.W, pady=(0, 10))
        
        # Documents treeview
        columns = ("name", "type", "date", "status")
        self.docs_tree = ttk.Treeview(self.documents_frame, columns=columns, show="headings")
        
        self.docs_tree.heading("name", text="Dokumentname")
        self.docs_tree.heading("type", text="Typ")
        self.docs_tree.heading("date", text="Erstellt")
        self.docs_tree.heading("status", text="Status")
        
        self.docs_tree.pack(fill=tk.BOTH, expand=True)
        
        # Buttons
        buttons_frame = ttk.Frame(self.documents_frame)
        buttons_frame.pack(fill=tk.X, pady=(10, 0))
        
        create_doc_btn = ttk.Button(buttons_frame, text="Neues Dokument", command=self.create_document)
        create_doc_btn.pack(side=tk.LEFT, padx=(0, 5))
        
    def create_search_tab(self):
        # Search frame
        search_frame = ttk.Frame(self.search_frame)
        search_frame.pack(fill=tk.X, pady=(0, 10))
        
        search_label = ttk.Label(search_frame, text="Recherche in Rechtsdatenbank:")
        search_label.pack(side=tk.LEFT)
        
        self.search_var = tk.StringVar()
        search_entry = ttk.Entry(search_frame, textvariable=self.search_var, width=50)
        search_entry.pack(side=tk.LEFT, padx=(10, 5))
        
        search_btn = ttk.Button(search_frame, text="Suchen", command=self.perform_search)
        search_btn.pack(side=tk.LEFT)
        
        # Results area
        results_label = ttk.Label(self.search_frame, text="Suchergebnisse:", font=("Arial", 10, "bold"))
        results_label.pack(anchor=tk.W, pady=(10, 5))
        
        self.results_text = tk.Text(self.search_frame, height=20)
        self.results_text.pack(fill=tk.BOTH, expand=True)
        
    def create_conversations_tab(self):
        # Conversation controls
        controls_frame = ttk.Frame(self.conversations_frame)
        controls_frame.pack(fill=tk.X, pady=(0, 10))
        
        self.record_btn = ttk.Button(controls_frame, text="Gespräch aufzeichnen", command=self.toggle_recording)
        self.record_btn.pack(side=tk.LEFT)
        
        send_btn = ttk.Button(controls_frame, text="An Server senden", command=self.send_conversation_to_server)
        send_btn.pack(side=tk.LEFT, padx=(5, 0))
        
        # Conversation input
        input_frame = ttk.Frame(self.conversations_frame)
        input_frame.pack(fill=tk.X, pady=(0, 10))
        
        ttk.Label(input_frame, text="Transkript hinzufügen:").pack(anchor=tk.W)
        self.transcript_var = tk.StringVar()
        transcript_entry = ttk.Entry(input_frame, textvariable=self.transcript_var, width=80)
        transcript_entry.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=(0, 5))
        
        add_btn = ttk.Button(input_frame, text="Hinzufügen", command=self.add_transcript)
        add_btn.pack(side=tk.LEFT)
        
        # Conversation display
        conv_label = ttk.Label(self.conversations_frame, text="Gesprächsverlauf:", font=("Arial", 10, "bold"))
        conv_label.pack(anchor=tk.W, pady=(10, 5))
        
        self.conv_text = tk.Text(self.conversations_frame, height=15)
        self.conv_text.pack(fill=tk.BOTH, expand=True)
        
        # Suggestions area
        suggestions_label = ttk.Label(self.conversations_frame, text="Vorschläge:", font=("Arial", 10, "bold"))
        suggestions_label.pack(anchor=tk.W, pady=(10, 5))
        
        self.suggestions_text = tk.Text(self.conversations_frame, height=8)
        self.suggestions_text.pack(fill=tk.BOTH, expand=True)
        
    def create_status_bar(self):
        self.status_var = tk.StringVar()
        self.status_var.set("Bereit")
        
        status_bar = ttk.Label(self.root, textvariable=self.status_var, relief=tk.SUNKEN, anchor=tk.W)
        status_bar.pack(side=tk.BOTTOM, fill=tk.X)
        
    def load_config(self):
        config_file = "config/default.json"
        if os.path.exists(config_file):
            try:
                with open(config_file, "r") as f:
                    self.config = json.load(f)
                    # Update conversation recorder with server URL
                    server_url = self.config.get("server_url", "http://localhost:8000")
                    self.conversation_recorder.set_server_url(server_url)
            except:
                self.config = {}
        else:
            self.config = {
                "server_url": "http://localhost:8000",
                "style_profile_id": None
            }
            self.save_config()
            
    def save_config(self):
        os.makedirs("config", exist_ok=True)
        with open("config/default.json", "w") as f:
            json.dump(self.config, f, indent=2)
            
    def new_case(self):
        # Create new case dialog
        case_window = tk.Toplevel(self.root)
        case_window.title("Neuer Fall")
        case_window.geometry("400x300")
        
        ttk.Label(case_window, text="Fallname:").pack(pady=(20, 5))
        name_entry = ttk.Entry(case_window, width=40)
        name_entry.pack(pady=5)
        
        ttk.Label(case_window, text="Mandant:").pack(pady=(10, 5))
        client_entry = ttk.Entry(case_window, width=40)
        client_entry.pack(pady=5)
        
        def save_case():
            name = name_entry.get()
            client = client_entry.get()
            if name and client:
                case_data = {
                    "id": hashlib.md5(f"{name}{client}{datetime.now()}".encode()).hexdigest()[:8],
                    "name": name,
                    "client": client,
                    "created": datetime.now().isoformat(),
                    "status": "Neu",
                    "documents": [],
                    "conversations": []
                }
                
                # Add to treeview
                self.cases_tree.insert("", "end", values=(name, client, datetime.now().strftime("%d.%m.%Y"), "Neu"))
                
                # Set client ID for conversation recorder
                self.conversation_recorder.set_client_id(case_data["id"])
                
                case_window.destroy()
                messagebox.showinfo("Erfolg", f"Fall '{name}' wurde erstellt.")
            else:
                messagebox.showerror("Fehler", "Bitte füllen Sie alle Felder aus.")
                
        save_btn = ttk.Button(case_window, text="Fall erstellen", command=save_case)
        save_btn.pack(pady=20)
        
    def open_case(self):
        messagebox.showinfo("Info", "Fall öffnen Funktion")
        
    def save_case(self):
        messagebox.showinfo("Info", "Fall speichern Funktion")
        
    def analyze_style(self):
        messagebox.showinfo("Info", "Stilanalyse Funktion")
        
    def create_document(self):
        messagebox.showinfo("Info", "Dokumentenerstellung Funktion")
        
    def record_conversation(self):
        # This opens the conversations tab and starts recording
        self.notebook.select(self.conversations_frame)
        self.toggle_recording()
        
    def server_settings(self):
        # Server settings dialog
        settings_window = tk.Toplevel(self.root)
        settings_window.title("Server-Einstellungen")
        settings_window.geometry("400x200")
        
        ttk.Label(settings_window, text="Server URL:").pack(pady=(20, 5))
        url_var = tk.StringVar(value=self.config.get("server_url", ""))
        url_entry = ttk.Entry(settings_window, textvariable=url_var, width=40)
        url_entry.pack(pady=5)
        
        def save_settings():
            self.config["server_url"] = url_var.get()
            self.save_config()
            # Update conversation recorder
            self.conversation_recorder.set_server_url(url_var.get())
            settings_window.destroy()
            messagebox.showinfo("Erfolg", "Einstellungen gespeichert.")
            
        save_btn = ttk.Button(settings_window, text="Speichern", command=save_settings)
        save_btn.pack(pady=20)
        
    def style_profiles(self):
        messagebox.showinfo("Info", "Stilprofile Funktion")
        
    def perform_search(self):
        query = self.search_var.get()
        if query:
            self.status_var.set("Suche läuft...")
            self.root.update()
            
            # Simulate search (in real implementation, this would call the server API)
            self.results_text.delete(1.0, tk.END)
            self.results_text.insert(tk.END, f"Suchergebnisse für: {query}\n\n")
            self.results_text.insert(tk.END, "1. § 1 KSchG - Allgemeiner Teil\n")
            self.results_text.insert(tk.END, "   Kündigungsschutzgesetz, § 1: Persönlicher Geltungsbereich\n\n")
            self.results_text.insert(tk.END, "2. BAG Urteil vom 15.03.2023 - 5 AZR 123/22\n")
            self.results_text.insert(tk.END, "   Kündigungsschutz bei Betriebsübergang\n\n")
            self.results_text.insert(tk.END, "3. § 611a BGB - Fortgeltung von Arbeitsverträgen\n")
            self.results_text.insert(tk.END, "   Bei Betriebsübergang fortgeltende Arbeitsverträge\n")
            
            self.status_var.set("Suche abgeschlossen")
        else:
            messagebox.showwarning("Warnung", "Bitte geben Sie einen Suchbegriff ein.")
            
    def toggle_recording(self):
        if self.record_btn.cget("text") == "Gespräch aufzeichnen":
            self.record_btn.config(text="Aufzeichnung stoppen")
            self.status_var.set("Aufzeichnung läuft...")
            self.conv_text.insert(tk.END, f"[{datetime.now().strftime('%H:%M:%S')}] Gespräch gestartet\n")
        else:
            self.record_btn.config(text="Gespräch aufzeichnen")
            self.status_var.set("Aufzeichnung gestoppt")
            self.conv_text.insert(tk.END, f"[{datetime.now().strftime('%H:%M:%S')}] Gespräch beendet\n")
            
            # Generate suggestions
            self.generate_suggestions()
    
    def add_transcript(self):
        transcript = self.transcript_var.get()
        if transcript:
            self.conversation_recorder.add_transcript(transcript)
            self.conv_text.insert(tk.END, f"[{datetime.now().strftime('%H:%M:%S')}] {transcript}\n")
            self.transcript_var.set("")
            
            # Auto-generate suggestions when adding transcript
            self.generate_suggestions()
    
    def generate_suggestions(self):
        summary = self.conversation_recorder.generate_summary()
        suggestions = summary.get("suggestions", [])
        
        self.suggestions_text.delete(1.0, tk.END)
        self.suggestions_text.insert(tk.END, "Vorschläge:\n")
        for suggestion in suggestions:
            self.suggestions_text.insert(tk.END, f"• {suggestion}\n")
    
    def send_conversation_to_server(self):
        self.status_var.set("Sende Gespräch an Server...")
        self.root.update()
        
        # Send conversation to server
        result = self.conversation_recorder.send_to_server()
        
        if result["success"]:
            self.status_var.set("Gespräch erfolgreich an Server gesendet")
            messagebox.showinfo("Erfolg", "Gespräch wurde erfolgreich an den Server gesendet.")
            
            # Get server summary
            server_summary = self.conversation_recorder.get_server_summary(result["data"])
            if server_summary["success"]:
                self.display_server_summary(server_summary["data"])
        else:
            self.status_var.set("Fehler beim Senden an Server")
            messagebox.showerror("Fehler", f"Fehler beim Senden an Server: {result['message']}")
    
    def display_server_summary(self, summary_data):
        self.suggestions_text.insert(tk.END, "\n--- Server-Zusammenfassung ---\n")
        self.suggestions_text.insert(tk.END, f"Themen: {', '.join(summary_data.get('topics', []))}\n")
        self.suggestions_text.insert(tk.END, f"Vorschläge:\n")
        for suggestion in summary_data.get('suggested_actions', []):
            self.suggestions_text.insert(tk.END, f"• {suggestion}\n")

def main():
    root = tk.Tk()
    app = LexWolfClient(root)
    root.mainloop()

if __name__ == "__main__":
    main()