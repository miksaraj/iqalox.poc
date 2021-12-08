package com.iqalox.tool

class GenerateAst {
    fun main(args: Array<String>): Unit {
        if (args.size != 1) {
            System.err.println("Usage: generate_ast <output directory>")
            System.exit(64)
        }
        val outputDir = args[0]
    }

    fun defineAst(outputDir: String, baseName: String, types: List<String>): Unit {
        val path = outputDir + "/" + baseName + ".kt"
    }

    fun defineVisitor(writer, baseName: String, types: List<String>): Unit {}

    fun defineType(writer, baseName: String, className: String, fieldList: String): Unit {}
}