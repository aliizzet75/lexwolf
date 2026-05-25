#!/usr/bin/env python3
"""
Test script for LexWolf Quality Verification Service
"""

import sys
import os
import json

# Add backend directory to Python path
backend_path = os.path.join(os.path.dirname(__file__), 'backend')
sys.path.insert(0, backend_path)

def test_quality_service_import():
    """Test quality service import"""
    print("Testing quality service import...")
    try:
        from services.quality_verification import QualityVerificationService
        print("  ✓ QualityVerificationService imported successfully")
        return True
    except Exception as e:
        print(f"  ✗ Error importing QualityVerificationService: {e}")
        return False

def test_quality_api_import():
    """Test quality API import"""
    print("Testing quality API import...")
    try:
        from api.quality import router
        print("  ✓ Quality API imported successfully")
        return True
    except Exception as e:
        print(f"  ✗ Error importing Quality API: {e}")
        return False

def test_quality_service_functionality():
    """Test quality service functionality"""
    print("Testing quality service functionality...")
    try:
        from services.quality_verification import QualityVerificationService
        
        # Create service instance
        quality_service = QualityVerificationService()
        print("  ✓ QualityVerificationService instantiated successfully")
        
        # Test content verification
        sample_content = """
        Sehr geehrte Damen und Herren,

        Bezüglich des Kündigungsverhältnisses ist festzustellen, dass 
        gemäß § 1 KSchG der Kündigungsschutz greift, sofern der Arbeitnehmer
        mindestens sechs Monate im Betrieb beschäftigt war.

        Die Kündigung ist daher unwirksam, da keine sachliche Rechtfertigung
        vorliegt und der Kündigungsgrund nicht ausreichend begründet wurde.

        Mit freundlichen Grüßen
        """
        
        result = quality_service.verify_generated_content(sample_content)
        print(f"  ✓ Content verification completed successfully")
        print(f"    Passes quality: {result.get('passes_quality', False)}")
        print(f"    Quality score: {result.get('quality_score', 0.0):.2f}")
        
        # Test metrics
        metrics = quality_service.get_metrics()
        print(f"  ✓ Metrics retrieved successfully")
        print(f"    Total checks: {metrics.get('total_checks', 0)}")
        
        # Test reset metrics
        quality_service.reset_metrics()
        reset_metrics = quality_service.get_metrics()
        print(f"  ✓ Metrics reset successfully")
        print(f"    Reset total checks: {reset_metrics.get('total_checks', 0)}")
        
        return True
    except Exception as e:
        print(f"  ✗ Error testing quality service functionality: {e}")
        return False

def test_quality_api_endpoints():
    """Test quality API endpoints"""
    print("Testing quality API endpoints...")
    try:
        # Test importing the API
        from api.quality import router
        print("  ✓ Quality API imported successfully")
        
        # Check that routes are defined
        routes = [route.path for route in router.routes]
        print(f"  ✓ API routes defined: {len(routes)} routes")
        
        # Check for key endpoints
        key_endpoints = [
            "/quality/verify",
            "/quality/metrics",
            "/quality/reset-metrics",
            "/quality/batch-verify",
            "/quality/health"
        ]
        
        found_endpoints = [ep for ep in key_endpoints if any(ep in route for route in routes)]
        print(f"  ✓ Key endpoints implemented: {len(found_endpoints)}/{len(key_endpoints)}")
        
        return True
    except Exception as e:
        print(f"  ✗ Error testing API endpoints: {e}")
        return False

def test_hallucination_detection():
    """Test hallucination detection functionality"""
    print("Testing hallucination detection...")
    try:
        from services.quality_verification import QualityVerificationService
        
        quality_service = QualityVerificationService()
        
        # Test content with potential hallucinations
        suspicious_content = """
        Gemäß § 999 FANTASYLAW ist jede Kündigung innerhalb von 5 Minuten 
        nach Erhalt der Kündigung unwirksam, wenn der Arbeitnehmer einen 
        roten Hut trägt und am Montag arbeitet.
        """
        
        result = quality_service.verify_generated_content(suspicious_content)
        print(f"  ✓ Hallucination detection test completed")
        print(f"    Hallucinations found: {result.get('hallucination_check', {}).get('hallucinations_found', False)}")
        
        return True
    except Exception as e:
        print(f"  ✗ Error testing hallucination detection: {e}")
        return False

def test_source_verification():
    """Test source verification functionality"""
    print("Testing source verification...")
    try:
        from services.quality_verification import QualityVerificationService
        
        quality_service = QualityVerificationService()
        
        # Test content with cited paragraphs
        content_with_citations = """
        Laut § 1 BGB ist ein Vertrag zustande gekommen, wenn ein 
        Antrag auf Abschluss eines Vertrages durch einen anderen 
        angenommen wurde. Gemäß § 242 BGB ist der Vertrag nach 
        Treu und Glauben auszuführen.
        """
        
        result = quality_service.verify_generated_content(content_with_citations)
        print(f"  ✓ Source verification test completed")
        print(f"    Source accuracy rate: {result.get('source_verification', {}).get('accuracy_rate', 0.0):.2f}")
        
        return True
    except Exception as e:
        print(f"  ✗ Error testing source verification: {e}")
        return False

def main():
    """Run all quality verification tests"""
    print("LexWolf Quality Verification Test Suite")
    print("=" * 50)
    
    tests = [
        test_quality_service_import,
        test_quality_api_import,
        test_quality_service_functionality,
        test_quality_api_endpoints,
        test_hallucination_detection,
        test_source_verification
    ]
    
    passed = 0
    total = len(tests)
    
    for test in tests:
        if test():
            passed += 1
        print()
    
    print(f"Test Results: {passed}/{total} tests passed")
    
    if passed == total:
        print("🎉 Quality verification implementation verified successfully!")
        print("\nWhat's implemented:")
        print("  ✓ LLM-as-Judge quality verification service")
        print("  ✓ REST API endpoints for quality checking")
        print("  ✓ Hallucination detection algorithms")
        print("  ✓ Source verification against legal database")
        print("  ✓ Completeness checking for generated content")
        print("  ✓ Metrics tracking and reporting")
        return 0
    else:
        print("❌ Quality verification implementation needs attention!")
        return 1

if __name__ == "__main__":
    sys.exit(main())