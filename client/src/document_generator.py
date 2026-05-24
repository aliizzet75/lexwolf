import json
from datetime import datetime
from typing import Dict, List

class DocumentGenerator:
    def __init__(self):
        self.templates = self._load_templates()
    
    def _load_templates(self):
        """
        Load document templates
        """
        # In a real implementation, these would be loaded from files
        templates = {
            "kündigungsschutzklage": {
                "title": "Kündigungsschutzklage",
                "sections": [
                    {
                        "name": "Überschrift",
                        "content": "{court}\n\n{name} {surname}\n{address}\n\ngegen\n\n{opponent_name}\n{opponent_address}\n\nKündigungsschutzklage"
                    },
                    {
                        "name": "Antrag",
                        "content": "1. Das Kündigungsverhältnis zwischen dem Kläger und dem Beklagten wird für unwirksam erklärt.\n2. Der Beklagte wird verurteilt, dem Kläger die aufgrund des Arbeitsvertrages geschuldete Vergütung fortzuzahlen."
                    },
                    {
                        "name": "Sachverhalt",
                        "content": "Der Kläger war vom {start_date} bis {end_date} als {position} beim Beklagten beschäftigt. Am {termination_date} erhielt der Kläger eine Kündigung des Arbeitsvertrages."
                    },
                    {
                        "name": "Rechtliche Würdigung",
                        "content": "Die Kündigung ist unwirksam, da der Kündigungsschutz nach § 1 KSchG greift und keine sachliche Rechtfertigung der Kündigung vorliegt."
                    },
                    {
                        "name": "Zumutbarkeit der Fortsetzung",
                        "content": "Die Fortsetzung des Arbeitsverhältnisses ist für den Beklagten zumutbar, da keine Umstände vorliegen, die eine Fortsetzung des Arbeitsverhältnisses für den Beklagten unzumutbar machen würden."
                    },
                    {
                        "name": "Ortstermin",
                        "content": "Die mündliche Verhandlung soll am {court_date} um {court_time} Uhr stattfinden."
                    },
                    {
                        "name": "Rechtsmittelbelehrung",
                        "content": "Gegen das Urteil des Arbeitsgerichts kann binnen eines Monats nach Zustellung beim Landesarbeitsgericht Berufung eingelegt werden."
                    }
                ]
            },
            "mahnbescheid": {
                "title": "Mahnbescheid",
                "sections": [
                    {
                        "name": "Überschrift",
                        "content": "{court}\n\n{name} {surname}\n{address}\n\ngegen\n\n{opponent_name}\n{opponent_address}\n\nMahnbescheid"
                    },
                    {
                        "name": "Antrag",
                        "content": "1. Der Beklagte wird aufgefordert, dem Kläger {amount} € nebst Verzugszinsen zu zahlen.\n2. Kosten des Mahnverfahrens in Höhe von {costs} €."
                    },
                    {
                        "name": "Sachverhalt",
                        "content": "Zwischen den Parteien besteht ein geschuldeter Betrag in Höhe von {amount} €. Der Beklagte wurde bereits mehrfach zur Zahlung aufgefordert, ist jedoch bisher nicht nachgekommen."
                    },
                    {
                        "name": "Rechtliche Würdigung",
                        "content": "Der Anspruch des Klägers ergibt sich aus {legal_basis}. Die Forderung ist fällig und unstrittig."
                    }
                ]
            },
            "vertrag": {
                "title": "Vertrag",
                "sections": [
                    {
                        "name": "Präambel",
                        "content": "Zwischen\n\n{name} {surname}\n{address}\n\nund\n\n{opponent_name}\n{opponent_address}\n\nwird folgender Vertrag geschlossen:"
                    },
                    {
                        "name": "Vertragsgegenstand",
                        "content": "§ 1 Vertragsgegenstand\n{subject}"
                    },
                    {
                        "name": "Leistungen",
                        "content": "§ 2 Leistungen\n{services}"
                    },
                    {
                        "name": "Vergütung",
                        "content": "§ 3 Vergütung\n{payment}"
                    },
                    {
                        "name": "Laufzeit",
                        "content": "§ 4 Laufzeit\n{duration}"
                    },
                    {
                        "name": "Schlussbestimmungen",
                        "content": "§ 5 Schlussbestimmungen\n{final_clauses}"
                    }
                ]
            }
        }
        return templates
    
    def generate_document(self, template_name: str, data: Dict, style_profile=None):
        """
        Generate document from template
        """
        if template_name not in self.templates:
            raise ValueError(f"Template '{template_name}' not found")
        
        template = self.templates[template_name]
        
        # Create document structure
        document = {
            "title": template["title"],
            "created": datetime.now().isoformat(),
            "sections": [],
            "metadata": {
                "template": template_name,
                "style_profile": style_profile
            }
        }
        
        # Fill template sections with data
        for section in template["sections"]:
            filled_content = section["content"].format(**data)
            document["sections"].append({
                "name": section["name"],
                "content": filled_content
            })
        
        return document
    
    def format_document(self, document: Dict, format_type: str = "text") -> str:
        """
        Format document for output
        """
        if format_type == "text":
            output = f"{document['title']}\n"
            output += "=" * len(document['title']) + "\n\n"
            
            for section in document["sections"]:
                output += f"{section['name']}\n"
                output += "-" * len(section['name']) + "\n"
                output += f"{section['content']}\n\n"
            
            return output
        
        elif format_type == "json":
            return json.dumps(document, ensure_ascii=False, indent=2)
        
        else:
            raise ValueError(f"Unsupported format type: {format_type}")
    
    def save_document(self, document: Dict, filename: str, format_type: str = "text"):
        """
        Save document to file
        """
        content = self.format_document(document, format_type)
        
        with open(filename, 'w', encoding='utf-8') as f:
            f.write(content)
        
        return True
    
    def list_templates(self) -> List[str]:
        """
        List available templates
        """
        return list(self.templates.keys())
    
    def get_template_info(self, template_name: str) -> Dict:
        """
        Get information about a template
        """
        if template_name not in self.templates:
            return None
        
        template = self.templates[template_name]
        return {
            "name": template_name,
            "title": template["title"],
            "sections": [section["name"] for section in template["sections"]]
        }

# Example usage
if __name__ == "__main__":
    generator = DocumentGenerator()
    
    # Show available templates
    print("Available templates:")
    for template in generator.list_templates():
        info = generator.get_template_info(template)
        print(f"  - {info['title']} ({template})")
        print(f"    Sections: {', '.join(info['sections'])}")
    
    # Generate a sample document
    print("\nGenerating sample document...")
    
    data = {
        "court": "Arbeitsgericht Berlin",
        "name": "Max",
        "surname": "Mustermann",
        "address": "Musterstraße 1, 12345 Berlin",
        "opponent_name": "Musterfirma GmbH",
        "opponent_address": "Firmenstraße 2, 12345 Berlin",
        "start_date": "01.01.2020",
        "end_date": "31.12.2023",
        "termination_date": "01.01.2023",
        "position": "Softwareentwickler",
        "court_date": "15.06.2023",
        "court_time": "10:00"
    }
    
    document = generator.generate_document("kündigungsschutzklage", data)
    
    # Format and display
    formatted = generator.format_document(document)
    print(formatted)
    
    # Save to file
    generator.save_document(document, "sample_klage.txt")
    print("Document saved to sample_klage.txt")