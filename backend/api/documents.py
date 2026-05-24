from fastapi import APIRouter, HTTPException, Depends
from typing import List, Optional, Dict, Any
from pydantic import BaseModel
import json
from datetime import datetime

router = APIRouter(prefix="/documents", tags=["documents"])

# Pydantic models for request/response
class DocumentTemplate(BaseModel):
    name: str
    title: str
    sections: List[Dict[str, str]]

class DocumentGenerateRequest(BaseModel):
    template_name: str
    data: Dict[str, Any]
    style_profile_id: Optional[str] = None

class DocumentGenerateResponse(BaseModel):
    document_id: str
    title: str
    created: str
    sections: List[Dict[str, str]]
    metadata: Dict[str, Any]

class TemplateInfo(BaseModel):
    name: str
    title: str
    sections: List[str]

class TemplateListResponse(BaseModel):
    templates: List[TemplateInfo]

# In-memory template storage (in a real implementation, this would be in a database)
TEMPLATES = {
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

@router.get("/", response_model=TemplateListResponse)
async def list_templates():
    """
    List available document templates
    """
    template_list = []
    for name, template in TEMPLATES.items():
        template_list.append(TemplateInfo(
            name=name,
            title=template["title"],
            sections=[section["name"] for section in template["sections"]]
        ))
    
    return TemplateListResponse(templates=template_list)

@router.get("/{template_name}", response_model=TemplateInfo)
async def get_template_info(template_name: str):
    """
    Get information about a specific template
    """
    if template_name not in TEMPLATES:
        raise HTTPException(status_code=404, detail=f"Template '{template_name}' not found")
    
    template = TEMPLATES[template_name]
    return TemplateInfo(
        name=template_name,
        title=template["title"],
        sections=[section["name"] for section in template["sections"]]
    )

@router.post("/", response_model=DocumentGenerateResponse)
async def generate_document(request: DocumentGenerateRequest):
    """
    Generate a document from a template
    """
    if request.template_name not in TEMPLATES:
        raise HTTPException(status_code=404, detail=f"Template '{request.template_name}' not found")
    
    template = TEMPLATES[request.template_name]
    
    # Create document structure
    document_id = f"doc_{datetime.now().strftime('%Y%m%d_%H%M%S')}"
    
    document = {
        "document_id": document_id,
        "title": template["title"],
        "created": datetime.now().isoformat(),
        "sections": [],
        "metadata": {
            "template": request.template_name,
            "style_profile_id": request.style_profile_id
        }
    }
    
    # Fill template sections with data
    for section in template["sections"]:
        try:
            filled_content = section["content"].format(**request.data)
        except KeyError as e:
            raise HTTPException(status_code=400, detail=f"Missing data field for template: {e}")
        
        document["sections"].append({
            "name": section["name"],
            "content": filled_content
        })
    
    return DocumentGenerateResponse(**document)

@router.post("/format")
async def format_document(document: DocumentGenerateResponse, format_type: str = "text"):
    """
    Format a generated document for output
    """
    if format_type == "text":
        output = f"{document.title}\n"
        output += "=" * len(document.title) + "\n\n"
        
        for section in document.sections:
            output += f"{section['name']}\n"
            output += "-" * len(section['name']) + "\n"
            output += f"{section['content']}\n\n"
        
        return {"content": output}
    
    elif format_type == "json":
        return {"content": document.json()}
    
    else:
        raise HTTPException(status_code=400, detail=f"Unsupported format type: {format_type}")

@router.get("/health")
async def health_check():
    """
    Health check endpoint
    """
    return {"status": "healthy", "service": "document_generation"}